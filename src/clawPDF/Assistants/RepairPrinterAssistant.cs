﻿using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Threading;
using clawSoft.clawPDF.Shared.Helper;
using clawSoft.clawPDF.Shared.ViewModels;
using clawSoft.clawPDF.Shared.Views;
using clawSoft.clawPDF.Utilities;
using clawSoft.clawPDF.Utilities.IO;
using NLog;
using pdfforge.DynamicTranslator;

namespace clawSoft.clawPDF.Assistants
{
    internal class RepairPrinterAssistant
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly IAssemblyHelper _assemblyHelper = new AssemblyHelper();
        private readonly IPathSafe _pathSafe = new PathWrapSafe();
        private readonly Translator _translator = TranslationHelper.Instance.TranslatorInstance;

        public bool TryRepairPrinter(IEnumerable<string> printerNames)
        {
            Logger.Error(
                "It looks like the printers are broken. This needs to be fixed to allow clawPDF to work properly");

            var title = _translator.GetTranslation("Application", "RepairPrinterNoPrintersInstalled",
                "No printers installed");
            var message = _translator.GetFormattedTranslation("Application", "RepairPrinterAskUser",
                "You do not have any clawPDF printers installed. Most likely there was a problem during the setup or the installation has been altered afterwards.\r\nDo you want to fix this by reinstalling the clawPDF printers?\r\n\r\nNote: You might be asked twice to grant admin privileges while fixing the problem.");

            Logger.Debug("Asking to start repair..");

            string[] label = { "Yes", "No" };
            label[0] = _translator.GetTranslation("MessageWindow", "Yes", "Yes");
            label[1] = _translator.GetTranslation("MessageWindow", "No", "No");

            if (MessageWindow.Show(message, title, label, MessageWindowButtons.YesNo, MessageWindowIcon.Exclamation) ==
                AdonisUI.Controls.MessageBoxResult.Yes)
            {
                var applicationPath = _assemblyHelper.GetCurrentAssemblyDirectory();
                var printerHelperPath = _pathSafe.Combine(applicationPath, "SetupHelper.exe");

                if (!File.Exists(printerHelperPath))
                {
                    Logger.Error("SetupHelper.exe does not exist!");
                    title = _translator.GetTranslation("Application", "Error", "Error");
                    message = _translator.GetFormattedTranslation("Application", "SetupFileMissing",
                        "An important clawPDF file is missing ('{0}'). Please reinstall clawPDF!",
                        _pathSafe.GetFileName(printerHelperPath));

                    string[] labels = { "OK" };
                    labels[0] = _translator.GetTranslation("MessageWindow", "Ok", "OK");

                    MessageWindow.Show(message, title, labels, MessageWindowButtons.OK, MessageWindowIcon.Error);
                    return false;
                }

                Logger.Debug("Reinstalling Printers...");

                CallProgramAsAdmin(printerHelperPath, "/Driver=Add");
                var installResult = CallProgramAsAdmin(printerHelperPath, "/Printer=Add /Name=clawPDF");
                Logger.Debug("Done: {0}", installResult);
            }

            WaitForPrintSpooler();

            Logger.Debug("Now we'll check again, if the printer is installed");
            if (IsRepairRequired())
            {
                Logger.Info("The printer could not be repaired.");
                title = _translator.GetTranslation("Application", "Error", "Error");
                message = _translator.GetFormattedTranslation("Application", "RepairPrinterFailed",
                    "clawPDF was not able to repair your printers. Please contact your administrator or the support to assist you in with this problem.");

                string[] labels = { "OK" };
                label[0] = _translator.GetTranslation("MessageWindow", "Ok", "OK");

                MessageWindow.Show(message, title, labels, MessageWindowButtons.OK, MessageWindowIcon.Exclamation);
                return false;
            }

            Logger.Info("The printer was repaired successfully");

            return true;
        }

        private bool CallProgramAsAdmin(string path, string arguments)
        {
            var shellExecuteHelper = new ShellExecuteHelper();
            var result = shellExecuteHelper.RunAsAdmin(path, arguments);

            if (result == ShellExecuteResult.RunAsWasDenied)
            {
                var message = TranslationHelper.Instance.TranslatorInstance.GetTranslation("ApplicationSettingsWindow",
                    "SufficientPermissions",
                    "Operation failed. You probably do not have sufficient permissions.");
                var caption =
                    TranslationHelper.Instance.TranslatorInstance.GetTranslation("ApplicationSettingsWindow", "Error",
                        "Error");

                string[] label = { "OK" };
                label[0] = _translator.GetTranslation("MessageWindow", "Ok", "OK");
                MessageWindow.Show(message, caption, label, MessageWindowButtons.OK, MessageWindowIcon.Error);

                return false;
            }

            return result == ShellExecuteResult.Success;
        }

        private string GetPrinterNameString(IEnumerable<string> printerNames)
        {
            var printers = printerNames.ToList();

            if (!printers.Any())
                printers.Add("clawPDF");

            return string.Join(" ", printers.Select(printerName => "\"" + printerName + "\""));
        }

        public bool IsRepairRequired()
        {
            var printerHelper = new PrinterHelper();
            return !printerHelper.GetclawPDFPrinters().Any();
        }

        public void WaitForPrintSpooler()
        {
            ServiceController printSpooler = new ServiceController("Spooler");

            Stopwatch stopwatch = Stopwatch.StartNew();

            while (printSpooler.Status != ServiceControllerStatus.Running && stopwatch.ElapsedMilliseconds < 120000)
            {
                printSpooler.Refresh();
                Thread.Sleep(3000);
            }

            stopwatch.Stop();

            if (printSpooler.Status != ServiceControllerStatus.Running)
            {
            }
        }
    }
}