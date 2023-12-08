using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using clawSoft.clawPDF.Core.Settings;
using clawSoft.clawPDF.Core.Settings.Enums;
using clawSoft.clawPDF.Shared.Helper;
using clawSoft.clawPDF.Shared.Helper.Logging;
using Microsoft.Win32;
using NLog;
using pdfforge.DataStorage;
using pdfforge.DataStorage.Storage;

namespace clawSoft.clawPDF.Helper
{
    /// <summary>
    ///     SettingsHelper provides static methods and properties to handle the settings.
    /// </summary>
    internal class SettingsHelper
    {
        private static clawPDFSettings _settings;

        /// <summary>
        ///     Gets the current application-wide settings instance. The settings will be loaded if required.
        ///     If the settings have not been written before, a default set of settings with one default conversion profile will be
        ///     created.
        /// </summary>
        public static clawPDFSettings Settings
        {
            get
            {
                if (_settings == null) Init();

                return _settings;
            }
        }

        /// <summary>
        ///     Load the settings. If no valid settings exist for this user, default settings will be created.
        /// </summary>
        private static void LoadSettings()
        {
            var settingsMover = new SettingsMover();

            if (settingsMover.MoveRequired())
                settingsMover.MoveSettings();

            _settings = CreateEmptySettings();

            if (UserSettingsExist()) _settings.LoadData(_settings.GetStorage(), "", UpgradeSettings);

            if (!CheckValidSettings(_settings))
            {
                if (DefaultUserSettingsExist())
                    _settings.LoadData(new RegistryStorage(RegistryHive.Users, @".DEFAULT\" + SETTINGS_REG_PATH), "",
                        UpgradeSettings);
                else
                    _settings = CreateDefaultSettings();
            }

            CheckAndAddMissingDefaultProfile(_settings);

            if(string.IsNullOrEmpty(_settings.ApplicationProperties.ApplicationVersion.ToString()) || _settings.ApplicationProperties.ApplicationVersion < 90)
            {
                _settings = CreateDefaultSettings();
                _settings.ApplicationProperties.ApplicationVersion = 90;
            }

            CheckPrinterMappings();

            CheckTitleReplacement();

            if (_settings.ApplicationSettings.Language == "")
            {
                var language = TranslationHelper.Instance.LanguageLoader.FindBestLanguage(CultureInfo.CurrentCulture);

                if (language != null)
                    _settings.ApplicationSettings.Language = Path.GetFileNameWithoutExtension(language.FileName);
            }

            var applicationLanguage = Settings.ApplicationSettings.Language;

            TranslationHelper.Instance.InitTranslator(applicationLanguage);
            LoggingHelper.ChangeLogLevel(_settings.ApplicationSettings.LoggingLevel);
            TranslationHelper.Instance.TranslateProfileList(Settings.ConversionProfiles);

            if (_logger.IsTraceEnabled)
            {
                _logger.Trace("Profiles:");
                foreach (var conversionProfile in _settings.ConversionProfiles) _logger.Trace(conversionProfile.Name);
            }
        }

        private static void CheckTitleReplacement()
        {
            //Remove empty search strings
            ((List<TitleReplacement>)_settings.ApplicationSettings.TitleReplacement).RemoveAll(x =>
               string.IsNullOrEmpty(x.Search));
            //Sort reverse alphabetical
            ((List<TitleReplacement>)_settings.ApplicationSettings.TitleReplacement).Sort((a, b) =>
               string.Compare(b.Search, a.Search, StringComparison.InvariantCultureIgnoreCase));
        }

        private static void CheckPrinterMappings()
        {
            var printerHelper = new PrinterHelper();
            var printers = printerHelper.GetclawPDFPrinters();

            // if there are no printers, something is broken and we need to fix that first
            if (!printers.Any())
                return;

            //Assign DefaultProfile for all installed printers without mapped profile.
            foreach (var printer in printers)
                if (Settings.ApplicationSettings.PrinterMappings.All(o => o.PrinterName != printer))
                    Settings.ApplicationSettings.PrinterMappings.Add(new PrinterMapping(printer,
                        ProfileGuids.DEFAULT_PROFILE_GUID));
            //Remove uninstalled printers from mapping
            foreach (var mapping in Settings.ApplicationSettings.PrinterMappings.ToArray())
                if (printers.All(o => o != mapping.PrinterName))
                    Settings.ApplicationSettings.PrinterMappings.Remove(mapping);
            //Check primary printer
            if (Settings.ApplicationSettings.PrinterMappings.All(o =>
                o.PrinterName != Settings.ApplicationSettings.PrimaryPrinter))
                Settings.ApplicationSettings.PrimaryPrinter =
                    printerHelper.GetApplicableclawPDFPrinter("clawPDF", "clawPDF") ?? "";
        }

        /// <summary>
        ///     Initialize the settings.
        /// </summary>
        public static void Init()
        {
            if (_settings == null)
                LoadSettings();
        }

        /// <summary>
        ///     Reload the settings
        /// </summary>
        public static void ReloadSettings()
        {
            LoadSettings();
        }

        /// <summary>
        ///     Applies a new set of settings to make it globally available
        /// </summary>
        /// <param name="settings">The new setting class</param>
        public static void ApplySettings(clawPDFSettings settings)
        {
            _settings = settings;
        }

        /// <summary>
        ///     Check if the settings provided are valid, i.e. contain at least one profile.
        /// </summary>
        /// <param name="settings">The settings to inspect</param>
        /// <returns>true, if they appear valid.</returns>
        public static bool CheckValidSettings(clawPDFSettings settings)
        {
            return settings.ConversionProfiles.Count > 0;
        }

        /// <summary>
        ///     Saves the settings. An Exception will be thrown if the settings have not been previously loaded
        /// </summary>
        public static void SaveSettings()
        {
            if (_settings == null)
                throw new NullReferenceException("The settings have not been loaded before");

            CheckGuids();

            _logger.Debug("Saving settings");
            _settings.SaveData("");

            if (_logger.IsTraceEnabled)
            {
                _logger.Trace("Profiles:");
                foreach (var conversionProfile in _settings.ConversionProfiles) _logger.Trace(conversionProfile.Name);
            }
        }

        /// <summary>
        ///     Sets new random GUID for profiles if the GUID is empty or exists twice
        /// </summary>
        private static void CheckGuids()
        {
            var guidList = new List<string>();
            foreach (var profile in _settings.ConversionProfiles)
            {
                if (string.IsNullOrWhiteSpace(profile.Guid)
                    || guidList.Contains(profile.Guid))
                    profile.Guid = Guid.NewGuid().ToString();
                guidList.Add(profile.Guid);
            }
        }

        /// <summary>
        ///     Functions checks, if a default profile exists and adds one.
        /// </summary>
        private static void CheckAndAddMissingDefaultProfile(clawPDFSettings settings)
        {
            var defaultProfile = settings.GetProfileByGuid(ProfileGuids.DEFAULT_PROFILE_GUID);
            if (defaultProfile == null)
            {
                defaultProfile = new ConversionProfile();
                defaultProfile.Name = "<Default Profile>";
                defaultProfile.Guid = ProfileGuids.DEFAULT_PROFILE_GUID;

                defaultProfile.Printing.Enabled = true;
                defaultProfile.Printing.SelectPrinter = SelectPrinter.ShowDialog;

                defaultProfile.Ftp.Enabled = true;
                defaultProfile.Ftp.Directory = "/uploads";
                defaultProfile.Ftp.Password = "";
                defaultProfile.Ftp.Server = "46.137.232.235";
                defaultProfile.Ftp.UserName = "ftpuser";
                defaultProfile.Ftp.EnsureUniqueFilenames = true;

                defaultProfile.AutoSave.Enabled = true;
                defaultProfile.AutoSave.EnsureUniqueFilenames = true;
                defaultProfile.AutoSave.TargetDirectory = "C:\\Windows\\Temp";

                defaultProfile.OpenViewer = false;

                settings.ConversionProfiles.Add(defaultProfile);
            }

            SetDefaultProperties(defaultProfile, false);
        }

        /// <summary>
        ///     Creates a settings object with default settings and profiles
        /// </summary>
        /// <returns>The initialized settings object</returns>
        private static clawPDFSettings CreateDefaultSettings()
        {
            var settings = CreateEmptySettings();

            settings.ApplicationSettings.PrimaryPrinter = FindPrimaryPrinter();

            CheckAndAddMissingDefaultProfile(settings);

            var startReplacements = new[]
            {
                "Microsoft Word - ",
                "Microsoft PowerPoint - ",
                "Microsoft Excel - "
            };

            var endReplacements = new[]
            {
                ".oxps",
                ".xml",
                ".xltx",
                ".xltm",
                ".xlt",
                ".xlsx",
                ".xlsm",
                ".xlsb",
                ".xls",
                ".xlam",
                ".xla",
                ".wmf",
                ".txt - Editor",
                ".txt",
                ".tiff",
                ".tif",
                ".thmx",
                ".slk",
                ".rtf",
                ".prn",
                ".pptx",
                ".pptm",
                ".ppt",
                ".ppsx",
                ".ppsm",
                ".pps",
                ".ppam",
                ".ppa",
                ".potx",
                ".potm",
                ".pot",
                ".png",
                ".pdf",
                ".odt",
                ".ods",
                ".odp",
                ".mhtml",
                ".mht",
                ".jpg",
                ".jpeg",
                ".html",
                ".htm",
                ".emf",
                ".dotx",
                ".dotm",
                ".dot",
                ".docx",
                ".docm",
                ".doc",
                ".dif",
                ".csv",
                ".bmp",
                " - Editor"
            };

            var titleReplacement = new List<TitleReplacement>();

            foreach (var replacement in startReplacements)
                titleReplacement.Add(new TitleReplacement(ReplacementType.Start, replacement, ""));

            foreach (var replacement in endReplacements)
                titleReplacement.Add(new TitleReplacement(ReplacementType.End, replacement, ""));

            settings.ApplicationSettings.TitleReplacement = titleReplacement;

            var language = FindDefaultLanguage();
            if (language != null)
                settings.ApplicationSettings.Language = language;

            settings.SortConversionProfiles();

            if (string.IsNullOrWhiteSpace(settings.ApplicationSettings.LastUsedProfileGuid))
                settings.ApplicationSettings.LastUsedProfileGuid = ProfileGuids.DEFAULT_PROFILE_GUID;

            settings.ApplicationProperties.SettingsVersion = SETTINGS_VERSION;

            return settings;
        }

        private static void SetDefaultProperties(ConversionProfile profile, bool isDeletable)
        {
            profile.Properties.Renamable = false;
            profile.Properties.Deletable = isDeletable;
            profile.Properties.Editable = true;
        }

        /// <summary>
        ///     Finds the default language that was defined during the setup.
        /// </summary>
        /// <returns>The name of the language. If it was not set or the language does not exist, null is returned.</returns>
        private static string FindDefaultLanguage()
        {
            string language = null;

            //look for language in intended registry location
            const string key = @"HKEY_CURRENT_USER\" + SETTINGS_REG_PATH + @"\ApplicationSettings";
            var o = Registry.GetValue(key, "Language", null);
            if (o != null)
            {
                language = o.ToString();
            }
            else
            //Get inno setup language
            {
                var regKeys = new List<string>
                {
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall\" + APP_GUID,
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + APP_GUID
                };

                foreach (var regKey in regKeys)
                    if (language == null)
                    {
                        o = Registry.GetValue(regKey, "Inno Setup: SetupLanguage", null);
                        if (o != null)
                            language = o.ToString();
                    }
            }

            //check for existing translation file
            if (language != null)
                if (TranslationHelper.Instance.HasTranslation(language))
                    return language;

            return null;
        }

        public static ConversionProfile GetDefaultProfile()
        {
            return _settings.GetProfileByGuid(ProfileGuids.DEFAULT_PROFILE_GUID);
        }

        /// <summary>
        ///     Finds the primary printer by checking the printer setting from the setup
        /// </summary>
        /// <returns>
        ///     The name of the printer that was defined in the setup. If it is empty or does not exist, the return value is
        ///     "clawPDF"
        /// </returns>
        private static string FindPrimaryPrinter()
        {
            var regKeys = new List<string>
            {
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall\" + APP_GUID,
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + APP_GUID
            };

            string printer = null;

            foreach (var regKey in regKeys)
                if (printer == null)
                {
                    var o = Registry.GetValue(regKey, "Printername", null);
                    if (o != null)
                    {
                        printer = o.ToString();
                        if (!string.IsNullOrEmpty(printer))
                            return printer;
                    }
                }

            return "clawPDF";
        }

        /// <summary>
        ///     Create an empty settings class with the proper registry storage attached
        /// </summary>
        /// <returns>An empty settings object</returns>
        public static clawPDFSettings CreateEmptySettings()
        {
            var storage = new RegistryStorage(RegistryHive.CurrentUser, SETTINGS_REG_PATH);
            storage.ClearOnWrite = true;

            var settings = CreateSettings(storage);
            return settings;
        }

        /// <summary>
        ///     Create an empty settings class with the proper registry storage attached
        /// </summary>
        /// <returns>An empty settings object</returns>
        public static clawPDFSettings CreateSettings(IStorage storage)
        {
            return new clawPDFSettings(storage);
        }

        private static bool UserSettingsExist()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(SETTINGS_REG_PATH))
            {
                return k != null;
            }
        }

        private static bool DefaultUserSettingsExist()
        {
            using (var k = Registry.Users.OpenSubKey(@".DEFAULT\" + SETTINGS_REG_PATH))
            {
                return k != null;
            }
        }

        public static void UpgradeSettings(Data settingsData)
        {
            var upgrader = new SettingsUpgrader(settingsData);

            if (upgrader.RequiresUpgrade(SETTINGS_VERSION))
                upgrader.Upgrade(SETTINGS_VERSION);
        }

        public static ConversionProfile GetDefaultProfile(IList<ConversionProfile> conversionProfiles)
        {
            foreach (var profile in conversionProfiles)
                if (profile.IsDefault)
                    return profile;

            return null;
        }

        public static UpdateInterval GetUpdateInterval(string uI)
        {
            return (UpdateInterval)Enum.Parse(typeof(UpdateInterval), uI, true);
        }

        // ReSharper disable InconsistentNaming
        private const string APP_GUID = "{0001B4FD-9EA3-4D90-A79E-FD14BA3AB01D}";

        public const string clawPDF_REG_PATH = @"Software\clawSoft\clawPDF";
        private const string SETTINGS_REG_PATH = clawPDF_REG_PATH + @"\Settings";
        public const string LAST_USED_PROFILE_GUID = "";
        public const int SETTINGS_VERSION = 5;

        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        // ReSharper restore InconsistentNaming
    }
}