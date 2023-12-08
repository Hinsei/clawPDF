using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using clawSoft.clawPDF.Core.Ghostscript;
using clawSoft.clawPDF.Core.Ghostscript.OutputDevices;
using clawSoft.clawPDF.Core.Jobs;
using NLog;

namespace clawSoft.clawPDF.Core.Actions
{
    /// <summary>
    ///     Implements the action to print the input files
    /// </summary>
    public class PrintingAction : IAction
    {
        private const int ActionId = 13;
        protected static Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly GhostScript _ghostscript;

        public PrintingAction(GhostScript ghostscript)
        {
            _ghostscript = ghostscript;
        }

        /// <summary>
        ///     Prints the input files to the configured printer
        /// </summary>
        /// <param name="job">The job to process</param>
        /// <returns>An ActionResult to determine the success and a list of errors</returns>
        public ActionResult ProcessJob(IJob job)
        {
            Logger.Debug("Launched Printing-Action");

            foreach (var file in job.OutputFiles)
            {
                Logger.Debug("Trying to print file");
                Logger.Debug(File.Exists(file));

                try
                {
                    Logger.Debug("Trying to print using process");
                    Process p = new Process();
                    p.StartInfo.FileName = file;
                    p.StartInfo.Verb = "PrintTo";
                    p.StartInfo.Arguments = job.Profile.Printing.PrinterName;
                    p.StartInfo.CreateNoWindow = true;
                    p.Start();

                }
                catch (Exception ex)
                {
                    Logger.Debug("Failed at process");
                    Logger.Debug(ex.Message);

                    return new ActionResult(ActionId, 999);
                }
            }

            return new ActionResult();

            //try
            //{
                //OutputDevice printingDevice = new PrintingDevice(job);
                //_ghostscript.Run(printingDevice, job.JobTempFolder);
                //return new ActionResult();
            //}
            //catch (Exception ex)
            //{
                //try
                //{
                    //var errorCode = Convert.ToInt32(ex.Message);
                    //return new ActionResult(ActionId, errorCode);
                //}
                //catch
                //{
                    //Logger.Error("Error while printing");
                    //return new ActionResult(ActionId, 999);
                //}
            //}
        }
    }
}

