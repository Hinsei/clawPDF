﻿using System;
using System.IO;
using clawSoft.clawPDF.Core.Settings.Enums;

namespace clawSoft.clawPDF.Core
{
    public class OutputFormatHelper
    {
        public static bool HasValidExtension(string file, OutputFormat outputFormat)
        {
            var extension = Path.GetExtension(file);

            if (extension == null)
                return false;

            switch (outputFormat)
            {
                case OutputFormat.Pdf:
                case OutputFormat.PdfA1B:
                case OutputFormat.PdfA2B:
                case OutputFormat.PdfA3B:
                case OutputFormat.PdfImage32:
                case OutputFormat.PdfImage24:
                case OutputFormat.PdfImage8:
                case OutputFormat.PdfOCR32:
                case OutputFormat.PdfOCR24:
                case OutputFormat.PdfOCR8:
                case OutputFormat.PdfX:
                    return extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase);

                case OutputFormat.Jpeg:
                    return extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                           extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);

                case OutputFormat.Png:
                    return extension.Equals(".png", StringComparison.OrdinalIgnoreCase);

                case OutputFormat.Tif:
                    return extension.Equals(".tif", StringComparison.OrdinalIgnoreCase) ||
                           extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase);

                case OutputFormat.SVG:
                    return extension.Equals(".svg", StringComparison.OrdinalIgnoreCase);

                //case OutputFormat.DOCX:
                //    return extension.Equals(".docx", StringComparison.OrdinalIgnoreCase);

                //case OutputFormat.XPS:
                //    return extension.Equals(".oxps", StringComparison.OrdinalIgnoreCase);

                case OutputFormat.OCRTxt:
                    return extension.Equals(".txt", StringComparison.OrdinalIgnoreCase);

                case OutputFormat.Txt:
                    return extension.Equals(".txt", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        public static string EnsureValidExtension(string file, OutputFormat outputFormat)
        {
            if (HasValidExtension(file, outputFormat))
                return file;

            switch (outputFormat)
            {
                case OutputFormat.Pdf:
                case OutputFormat.PdfA1B:
                case OutputFormat.PdfA2B:
                case OutputFormat.PdfA3B:
                case OutputFormat.PdfImage32:
                case OutputFormat.PdfImage24:
                case OutputFormat.PdfImage8:
                case OutputFormat.PdfOCR32:
                case OutputFormat.PdfOCR24:
                case OutputFormat.PdfOCR8:
                case OutputFormat.PdfX:
                    return Path.ChangeExtension(file, ".pdf");

                case OutputFormat.Jpeg:
                    return Path.ChangeExtension(file, ".jpg");

                case OutputFormat.Png:
                    return Path.ChangeExtension(file, ".png");

                case OutputFormat.Tif:
                    return Path.ChangeExtension(file, ".tif");

                case OutputFormat.SVG:
                    return Path.ChangeExtension(file, ".svg");

                //case OutputFormat.DOCX:
                //    return Path.ChangeExtension(file, ".docx");

                //case OutputFormat.XPS:
                //    return Path.ChangeExtension(file, ".oxps");

                case OutputFormat.OCRTxt:
                case OutputFormat.Txt:
                    return Path.ChangeExtension(file, ".txt");
            }

            return file;
        }
    }
}