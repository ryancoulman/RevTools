using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace ValveGetter.Settings
{
    /// <summary>
    /// Pure data representation of valve extraction settings.
    /// Contains only parameters, not file or Revit logic.
    /// </summary>
    public class ValveServiceSettings
    {
        public double ToleranceMm { get; set; }
        public double TouchingDistMm { get; set; }
        public DebugLevel DebugMode { get; set; }
        public bool WriteToParameters { get; set; }
        public string InputParameterName { get; set; }
        public string OutputParameterName { get; set; }
        public ValveCollectionScope CollectionScope { get; set; }
        public bool AllowSelectionOverrides { get; set; }
        public List<CategoryFilter> ValveCategoryFilters { get; set; }
        public List<CategoryFilter> MEPCategoryFilters { get; set; }
    }

    /// <summary>
    /// Settings for valve service extraction with built-in file I/O
    /// </summary>
    public class ValveServiceSettingsManager
    {
        private const string DEFAULT_FILENAME = "ValveServiceSettings_Default.json";
        private const string PROFILES_FOLDER = "Profiles";


        /// <summary>
        /// Creates a new default settings configuration
        /// </summary>
        public static ValveServiceSettings CreateDefault()
        {
            return new ValveServiceSettings
            {
                ToleranceMm = 50.0,
                TouchingDistMm = 5.0,
                DebugMode = DebugLevel.Concise,
                WriteToParameters = true,
                InputParameterName = "ServiceName",
                OutputParameterName = "Comments",
                CollectionScope = ValveCollectionScope.EntireProject,
                AllowSelectionOverrides = true,
                ValveCategoryFilters = new List<CategoryFilter>
                {
                    new CategoryFilter
                    {
                        CategoryId = -2008055, // OST_PipeAccessory Id
                        CategoryName = "Pipe Accessories",
                        NameCondition = "valve",
                        ConditionTarget = FilterTarget.FamilyName
                    }
                },
                MEPCategoryFilters = new List<CategoryFilter>
                {
                    new CategoryFilter
                    {
                        CategoryId = -2008208, // Fabrication Pipework
                        CategoryName = "MEP Fabrication Pipework",
                        NameCondition = "",
                        ConditionTarget = FilterTarget.FamilyName
                    }
                }
            };
        }

        /// <summary>
        /// Loads the default settings from file, or creates new default if file doesn't exist
        /// </summary>
        public static ValveServiceSettings LoadDefault()
        {
            string filePath = GetDefaultSettingsPath();

            if (File.Exists(filePath))
            {
                try
                {
                    string json = File.ReadAllText(filePath);
                    return JsonConvert.DeserializeObject<ValveServiceSettings>(json);
                }
                catch
                {
                    // If load fails, return new default
                    return CreateDefault();
                }
            }
            else
            {
                // No default exists, create and save one
                ValveServiceSettings settings = CreateDefault();
                SaveAsDefault(settings);
                return settings;
            }
        }

        /// <summary>
        /// Saves current settings as the default
        /// </summary>
        public static void SaveAsDefault(ValveServiceSettings settings)
        {
            string filePath = GetDefaultSettingsPath();
            Save(settings, filePath);
        }

        /// <summary>
        /// Saves settings to a named profile
        /// </summary>
        public static void SaveAsProfile(ValveServiceSettings settings, string profileName)
        {
            string filePath = GetProfilePath(profileName);
            Save(settings, filePath);
        }

        /// <summary>
        /// Loads settings from a named profile
        /// </summary>
        public static ValveServiceSettings LoadProfile(string profileName)
        {
            string filePath = GetProfilePath(profileName);

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Profile '{profileName}' not found.");
            }

            string json = File.ReadAllText(filePath);
            return JsonConvert.DeserializeObject<ValveServiceSettings>(json);
        }

        /// <summary>
        /// Gets list of all saved profile names
        /// </summary>
        public static List<string> GetAvailableProfiles()
        {
            string profilesFolder = GetProfilesFolder();
            var profiles = new List<string>();

            if (Directory.Exists(profilesFolder))
            {
                var files = Directory.GetFiles(profilesFolder, "*.json");
                foreach (var file in files)
                {
                    profiles.Add(Path.GetFileNameWithoutExtension(file));
                }
            }

            return profiles;
        }

        /// <summary>
        /// Deletes a named profile
        /// </summary>
        public static bool DeleteProfile(string profileName)
        {
            string filePath = GetProfilePath(profileName);

            if (File.Exists(filePath))
            {
                try
                {
                    File.Delete(filePath);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        /// <summary>
        /// Private save method used by SaveAsDefault and SaveAsProfile
        /// </summary>
        private static void Save(ValveServiceSettings settings, string filePath)
        {
            try
            {
                // Ensure directory exists
                string directory = Path.GetDirectoryName(filePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Serialize and save
                string json = JsonConvert.SerializeObject(settings, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                throw new IOException($"Failed to save settings: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Gets the base folder for all settings
        /// </summary>
        private static string GetSettingsFolder()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "ValveServiceExtractor");
        }

        /// <summary>
        /// Gets the path to the default settings file
        /// </summary>
        private static string GetDefaultSettingsPath()
        {
            return Path.Combine(GetSettingsFolder(), DEFAULT_FILENAME);
        }

        /// <summary>
        /// Gets the profiles folder path
        /// </summary>
        private static string GetProfilesFolder()
        {
            return Path.Combine(GetSettingsFolder(), PROFILES_FOLDER);
        }

        /// <summary>
        /// Gets the path for a named profile
        /// </summary>
        private static string GetProfilePath(string profileName)
        {
            // Sanitize profile name
            string safeName = string.Join("_", profileName.Split(Path.GetInvalidFileNameChars()));
            return Path.Combine(GetProfilesFolder(), $"{safeName}.json");
        }
    }

    public class CategoryFilter
    {
        public int CategoryId { get; set; }
        public string CategoryName { get; set; }
        public string NameCondition { get; set; }
        public FilterTarget ConditionTarget { get; set; }
    }

    public enum FilterTarget
    {
        FamilyName,
        TypeName
    }

    public enum ValveCollectionScope
    {
        ActiveView,
        EntireProject,
    }

    public enum DebugLevel
    {
        None,
        Concise,
        Full
    }
}