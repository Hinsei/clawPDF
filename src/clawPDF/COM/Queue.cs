﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using clawSoft.clawPDF.Core.Jobs;
using clawSoft.clawPDF.Core.Settings;
using clawSoft.clawPDF.Core.Settings.Enums;
using clawSoft.clawPDF.Helper;
using clawSoft.clawPDF.Shared.Helper;
using clawSoft.clawPDF.Shared.Helper.Logging;
using clawSoft.clawPDF.Threading;
using clawSoft.clawPDF.Utilities.Communication;
using NLog;

namespace clawSoft.clawPDF.COM
{
    [ComVisible(true)]
    [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    [Guid("B68E141F-2671-4D2F-AA59-73C659AC57B5")]
    public interface IQueue
    {
        void Initialize();

        void SetProfileSetting(string name, string value);

        bool WaitForJob(int timeOut);

        PrintJob WaitForFirstJob();

        bool WaitForJobs(int jobCount, int timeOut);

        int Count { get; }

        PrintJob NextJob { get; }

        PrintJob GetJobByIndex(int jobIndex);

        void MergeJobs(PrintJob job1, PrintJob job2);

        void MergeAllJobs();

        void Clear();

        void DeleteJob(int index);

        void ReleaseCom();
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    [Guid("4655FAF4-5973-47C7-A7FC-417D105DDEA0")]
    [ProgId("clawPDF.JobQueue")]
    public class Queue : IQueue
    {
        private static readonly Logger ComLogger = LogManager.GetCurrentClassLogger();
        private readonly AutoResetEvent _newJobResetEvent = new AutoResetEvent(false);
        private readonly ThreadPool _threadPool = ThreadPool.Instance;
        private JobInfoQueue _comJobInfoQueue;
        private bool _isComActive;

        private readonly List<string> _unaccessibleItems = new List<string>
            {"autosave", "properties", "skipprintdialog", "savedialog"};

        private bool IsServerInstanceRunning => PipeServer.SessionServerInstanceRunning(ThreadManager.PipeName);

        /// <summary>
        ///     Set a conversion profile property using two strings: One for the name (i.e. PdfSettings.Security.Enable) and one
        ///     for the value
        /// </summary>
        /// <param name="name">Name of the setting. This can include subproperties (i.e. PdfSettings.Security.Enable)</param>
        /// <param name="value">A string that can be parsed to the type</param>
        public void SetProfileSetting(string name, string value)
        {
            var settings = SettingsHelper.Settings;
            var profile = settings.GetProfileByGuid(ProfileGuids.DEFAULT_PROFILE_GUID);
            if (profile != null)
            {
                if (HasAccess(name) && ValueReflector.HasProperty(profile, name))
                {
                    ValueReflector.SetPropertyValue(profile, name, value);
                    SettingsHelper.ApplySettings(settings);
                    SettingsHelper.Init();
                }
                else
                    throw new COMException("Invalid property name");
            }
            else
                throw new COMException("Invalid profile");
        }

        /// <summary>
        ///     Checks if the property name is accessible through COM
        /// </summary>
        /// <param name="propertyName">The property to check for</param>
        /// <returns>True, if the property can be accessed otherwise false</returns>
        private bool HasAccess(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName))
                return false;

            return !_unaccessibleItems.Any(item => propertyName.ToLowerInvariant().StartsWith(item));
        }

        /// <summary>
        ///     Initializes the essential components like JobInfoQueue for the COM object
        /// </summary>
        public void Initialize()
        {
            ComLogger.Trace("COM: Starting initialization process");
            _comJobInfoQueue = JobInfoQueue.Instance;
            _comJobInfoQueue.OnNewJobInfo += (sender, eventArgs) => NewJob();
            _isComActive = true;

            if (IsServerInstanceRunning)
                throw new InvalidOperationException(
                    "Access forbidden. An instance of clawPDF is currently running.");

            LoggingHelper.InitFileLogger("clawPDF", LoggingLevel.Error);
            SettingsHelper.Init();

            ComLogger.Trace("COM: Starting pipe server thread");
            ThreadManager.Instance.StartPipeServerThread();
        }

        /// <summary>
        ///     Waits for exactly one job to enter the queue
        /// </summary>
        /// <param name="timeOut">Duration which the queue should wait for a job</param>
        /// <returns>False, if the duration was exceeded. Otherwise it returns true</returns>
        public PrintJob WaitForFirstJob()
        {
            WaitForJobs(1, 9999);
            return JobById(0);
        }

        /// <summary>
        ///     Waits for exactly one job to enter the queue
        /// </summary>
        /// <param name="timeOut">Duration which the queue should wait for a job</param>
        /// <returns>False, if the duration was exceeded. Otherwise it returns true</returns>
        public bool WaitForJob(int timeOut)
        {
            return WaitForJobs(1, TimeSpan.FromSeconds(timeOut));
        }

        /// <summary>
        ///     Waits for n jobs to enter the queue
        /// </summary>
        /// <param name="jobCount">Number of jobs to wait for</param>
        /// <param name="timeOut">Duration which the queue should wait for the n jobs</param>
        /// <returns>False, if the duration was exceeded. Otherwise it returns true</returns>
        public bool WaitForJobs(int jobCount, int timeOut)
        {
            var ts = TimeSpan.FromSeconds(timeOut);
            return WaitForJobs(jobCount, ts);
        }

        /// <summary>
        ///     Returns the number of jobs in the queue
        /// </summary>
        public int Count => _comJobInfoQueue.Count;

        /// <summary>
        ///     Returns the next job in the queue as a ComJob
        /// </summary>
        public PrintJob NextJob => JobById(0);

        /// <summary>
        ///     Creates the job from the queue by index
        /// </summary>
        /// <param name="jobIndex">Index of the jobinfo in the queue</param>
        /// <returns>The corresponding ComJob</returns>
        public PrintJob GetJobByIndex(int jobIndex)
        {
            return JobById(jobIndex);
        }

        /// <summary>
        ///     Merges two ComJobs
        /// </summary>
        /// <param name="job1">The first job to merge</param>
        /// <param name="job2">The second job to merge</param>
        public void MergeJobs(PrintJob job1, PrintJob job2)
        {
            ComLogger.Trace("COM: Merging two ComJobs.");
            job1.JobInfo.Merge(job2.JobInfo);
            _comJobInfoQueue.Remove(job2.JobInfo);
        }

        /// <summary>
        ///     Merges all jobs in the queue
        /// </summary>
        public void MergeAllJobs()
        {
            if (_comJobInfoQueue.Count == 0)
                throw new COMException("The queue must not be empty.");

            ComLogger.Trace("COM: Merging all ComJobs.");
            while (_comJobInfoQueue.Count > 1)
            {
                var firstJob = _comJobInfoQueue.JobInfos[0];
                var nextJob = _comJobInfoQueue.JobInfos[1];

                firstJob.Merge(nextJob);
                _comJobInfoQueue.Remove(nextJob);
            }
        }

        /// <summary>
        ///     Remove all elements from the Queue
        /// </summary>
        public void Clear()
        {
            while (_comJobInfoQueue.JobInfos.Count > 0) _comJobInfoQueue.Remove(_comJobInfoQueue.JobInfos[0], true);
        }

        /// <summary>
        ///     Deletes a chosen print job.
        /// </summary>
        /// <param name="index">Determines the print job to be removed by its position in the queue.</param>
        public void DeleteJob(int index)
        {
            if (index < 0 || index >= Count)
                throw new COMException("The given index was out of range.");

            _comJobInfoQueue.Remove(_comJobInfoQueue.JobInfos[index], true);
        }

        /// <summary>
        ///     Shuts down the used instance
        /// </summary>
        public void ReleaseCom()
        {
            if (_isComActive)
            {
                if (!_threadPool.Join(4000))
                    throw new COMException("One of the thread jobs didn't finish within the time out.");

                ComLogger.Trace("COM: Cleaning up COM resources.");
                if (IsServerInstanceRunning)
                    ThreadManager.Instance.PipeServer.Stop();

                _isComActive = false;

                ComLogger.Trace("COM: Emptying queue.");
                EmptyQueue();
            }
            else
            {
                throw new COMException("No COM Instance was found.");
            }
        }

        /// <summary>
        ///     Waits for n jobs to enter the queue
        /// </summary>
        /// <param name="jobCount">Number of jobs to wait for</param>
        /// <param name="timeOut">Duration which the queue should wait for a job</param>
        /// <returns>
        ///     False, if the duration was exceeded or the queue contains more or as many jobs as specified by jobCount.
        ///     Otherwise it returns true
        /// </returns>
        private bool WaitForJobs(int jobCount, TimeSpan timeOut)
        {
            if (!_isComActive)
                throw new COMException("No COM instance was found. Initialize the object first.");

            ComLogger.Trace("Waiting for {0} jobs for {1} seconds", jobCount, timeOut);

            var maxTime = DateTime.Now + timeOut;

            while (_comJobInfoQueue.Count < jobCount)
            {
                if (DateTime.Now > maxTime)
                    return false;

                _newJobResetEvent.WaitOne(timeOut);
            }

            return true;
        }

        /// <summary>
        ///     Calls the Set-Method of the AutoResetEvent when a new job enters the queue
        /// </summary>
        private void NewJob()
        {
            _newJobResetEvent.Set();
        }

        /// <summary>
        ///     Creates the job from the queue by index
        /// </summary>
        /// <param name="id">Index of the jobinfo in the queue</param>
        /// <returns>The corresponding ComJob</returns>
        private PrintJob JobById(int id)
        {
            try
            {
                var currentJobInfo = _comJobInfoQueue.JobInfos[id];

                var jobTranslations = new JobTranslations();
                jobTranslations.EmailSignature = MailSignatureHelper.ComposeMailSignature();

                IJob currentJob = new GhostscriptJob(currentJobInfo, SettingsHelper.GetDefaultProfile(),
                    JobInfoQueue.Instance, jobTranslations);

                ComLogger.Trace("COM: Creating the ComJob from the queue determined by the index.");
                var indexedJob = new PrintJob(currentJob, _comJobInfoQueue);
                return indexedJob;
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new COMException("Invalid index. Please check the index parameter.");
            }
        }

        /// <summary>
        ///     In case something went wrong, i.e. an exception was thrown, empties the whole queue.
        /// </summary>
        private void EmptyQueue()
        {
            while (_comJobInfoQueue.JobInfos.Count > 0) _comJobInfoQueue.Remove(_comJobInfoQueue.JobInfos[0], true);
            _comJobInfoQueue = null;
        }
    }
}