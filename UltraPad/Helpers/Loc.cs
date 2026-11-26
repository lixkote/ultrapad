using System;
using Windows.ApplicationModel.Resources;

namespace UltraPad.Helpers
{
    public static class Loc
    {
        public static string Get(string key, string fallback)
        {
            try
            {
                string value = ResourceLoader.GetForCurrentView().GetString(key);
                return string.IsNullOrEmpty(value) ? fallback : value;
            }
            catch
            {
                return fallback;
            }
        }

        public static string Format(string key, string fallback, params object[] args)
        {
            try { return string.Format(Get(key, fallback), args); }
            catch { return string.Format(fallback, args); }
        }
    }
}
