using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WordPad.Helpers
{
    public static class SpellCheckHelper
    {
        #region COM interop

        private enum CORRECTIVE_ACTION
        {
            NONE = 0,
            GET_SUGGESTIONS = 1,
            REPLACE = 2,
            DELETE = 3
        }

        [ComImport, Guid("B7C82D61-FBE8-4B47-9B27-6C0D2E0DE0A3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISpellingError
        {
            uint get_StartIndex();
            uint get_Length();
            CORRECTIVE_ACTION get_CorrectiveAction();
            [return: MarshalAs(UnmanagedType.LPWStr)]
            string get_Replacement();
        }

        [ComImport, Guid("803E3BD4-2828-4410-8290-418D1D73C762"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IEnumSpellingError
        {
            [PreserveSig]
            int Next(out ISpellingError value);
        }

        [ComImport, Guid("00000101-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IEnumString
        {
            [PreserveSig]
            int Next(uint celt, [MarshalAs(UnmanagedType.LPWStr)] out string rgelt, out uint pceltFetched);
            [PreserveSig]
            int Skip(uint celt);
            void Reset();
            IntPtr Clone();
        }

        [ComImport, Guid("B6FD0B71-E2BC-4653-8D05-F197E412770B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISpellChecker
        {
            [return: MarshalAs(UnmanagedType.LPWStr)]
            string get_LanguageTag();
            IEnumSpellingError Check([MarshalAs(UnmanagedType.LPWStr)] string text);
            IEnumString Suggest([MarshalAs(UnmanagedType.LPWStr)] string word);
            void Add([MarshalAs(UnmanagedType.LPWStr)] string word);
            void Ignore([MarshalAs(UnmanagedType.LPWStr)] string word);
            void AutoCorrect([MarshalAs(UnmanagedType.LPWStr)] string from, [MarshalAs(UnmanagedType.LPWStr)] string to);
            byte GetOptionValue([MarshalAs(UnmanagedType.LPWStr)] string optionId);
            IntPtr get_OptionIds();
            IntPtr get_Id();
            IntPtr get_LocalizedName();
            uint add_SpellCheckerChanged(IntPtr handler);
            void remove_SpellCheckerChanged(uint eventCookie);
            IntPtr GetOptionDescription([MarshalAs(UnmanagedType.LPWStr)] string optionId);
            IEnumSpellingError ComprehensiveCheck([MarshalAs(UnmanagedType.LPWStr)] string text);
        }

        [ComImport, Guid("8E018A9D-2415-4677-BF08-794EA61F94BB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISpellCheckerFactory
        {
            IntPtr get_SupportedLanguages();
            int IsSupported([MarshalAs(UnmanagedType.LPWStr)] string languageTag);
            ISpellChecker CreateSpellChecker([MarshalAs(UnmanagedType.LPWStr)] string languageTag);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MULTI_QI
        {
            public IntPtr pIID;
            public IntPtr pItf;
            public int hr;
        }

        [DllImport("api-ms-win-core-com-l1-1-0.dll", ExactSpelling = true)]
        private static extern int CoCreateInstanceFromApp(
            [In] ref Guid rclsid, IntPtr punkOuter, uint dwClsCtx, IntPtr reserved, uint dwCount, [In, Out] MULTI_QI[] pResults);

        private const uint CLSCTX_INPROC_SERVER = 0x1;
        private static readonly Guid CLSID_SpellCheckerFactory = new Guid("7AB36653-1796-484B-BDFA-E74F1DB7C1DC");
        private static readonly Guid IID_ISpellCheckerFactory = new Guid("8E018A9D-2415-4677-BF08-794EA61F94BB");

        #endregion

        private static ISpellCheckerFactory _factory;
        private static bool _factoryFailed;
        private static readonly Dictionary<string, ISpellChecker> _checkers = new Dictionary<string, ISpellChecker>(StringComparer.OrdinalIgnoreCase);

        private static ISpellCheckerFactory Factory
        {
            get
            {
                if (_factory != null || _factoryFailed) return _factory;
                IntPtr iid = IntPtr.Zero;
                try
                {
                    iid = Marshal.AllocHGlobal(16);
                    Marshal.Copy(IID_ISpellCheckerFactory.ToByteArray(), 0, iid, 16);
                    var qi = new MULTI_QI[] { new MULTI_QI { pIID = iid } };
                    Guid clsid = CLSID_SpellCheckerFactory;
                    int hr = CoCreateInstanceFromApp(ref clsid, IntPtr.Zero, CLSCTX_INPROC_SERVER, IntPtr.Zero, 1, qi);
                    if (hr >= 0 && qi[0].hr >= 0 && qi[0].pItf != IntPtr.Zero)
                    {
                        object obj = Marshal.GetObjectForIUnknown(qi[0].pItf);
                        Marshal.Release(qi[0].pItf);
                        _factory = (ISpellCheckerFactory)obj;
                    }
                    else
                    {
                        _factoryFailed = true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("SpellCheckHelper: factory unavailable: " + ex.Message);
                    _factoryFailed = true;
                }
                finally
                {
                    if (iid != IntPtr.Zero) Marshal.FreeHGlobal(iid);
                }
                return _factory;
            }
        }

        public static string CurrentLanguageTag
        {
            get
            {
                var candidates = new List<string>();
                try { candidates.Add(Windows.Globalization.Language.CurrentInputMethodLanguageTag); } catch { }
                try { candidates.AddRange(Windows.System.UserProfile.GlobalizationPreferences.Languages); } catch { }
                candidates.Add("en-US");

                foreach (string tag in candidates)
                {
                    if (!string.IsNullOrEmpty(tag) && GetChecker(tag) != null) return tag;
                }
                return null;
            }
        }

        private static ISpellChecker GetChecker(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return null;
            if (_checkers.TryGetValue(tag, out ISpellChecker cached)) return cached;

            ISpellChecker checker = null;
            try
            {
                var factory = Factory;
                if (factory != null && factory.IsSupported(tag) != 0)
                {
                    checker = factory.CreateSpellChecker(tag);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("SpellCheckHelper: no checker for " + tag + ": " + ex.Message);
            }
            _checkers[tag] = checker;
            return checker;
        }

        public static string GetAutoCorrection(string word)
        {
            if (string.IsNullOrWhiteSpace(word)) return null;
            ISpellChecker checker = GetChecker(CurrentLanguageTag);
            if (checker == null) return null;
            try
            {
                IEnumSpellingError errors = checker.Check(word);
                while (errors.Next(out ISpellingError error) == 0 && error != null)
                {
                    if (error.get_StartIndex() == 0 && error.get_Length() == word.Length &&
                        error.get_CorrectiveAction() == CORRECTIVE_ACTION.REPLACE)
                    {
                        string replacement = error.get_Replacement();
                        if (!string.IsNullOrEmpty(replacement) && replacement != word) return replacement;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("SpellCheckHelper.GetAutoCorrection: " + ex.Message);
            }
            return null;
        }

        public static bool IsMisspelled(string word, int max, out List<string> suggestions)
        {
            suggestions = new List<string>();
            if (string.IsNullOrWhiteSpace(word)) return false;
            ISpellChecker checker = GetChecker(CurrentLanguageTag);
            if (checker == null) return false;
            try
            {
                bool misspelled = false;
                IEnumSpellingError errors = checker.Check(word);
                while (errors.Next(out ISpellingError error) == 0 && error != null)
                {
                    misspelled = true;
                    var action = error.get_CorrectiveAction();
                    if (action == CORRECTIVE_ACTION.REPLACE)
                    {
                        string r = error.get_Replacement();
                        if (!string.IsNullOrEmpty(r)) suggestions.Add(r);
                    }
                }
                if (!misspelled) return false;

                IEnumString list = checker.Suggest(word);
                while (suggestions.Count < max && list.Next(1, out string s, out uint fetched) == 0 && fetched == 1)
                {
                    if (!string.IsNullOrEmpty(s) && !suggestions.Contains(s)) suggestions.Add(s);
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("SpellCheckHelper.IsMisspelled: " + ex.Message);
                return false;
            }
        }

        public static void AddToDictionary(string word)
        {
            try { GetChecker(CurrentLanguageTag)?.Add(word); }
            catch (Exception ex) { Debug.WriteLine("SpellCheckHelper.AddToDictionary: " + ex.Message); }
        }

        public static void Ignore(string word)
        {
            try { GetChecker(CurrentLanguageTag)?.Ignore(word); }
            catch (Exception ex) { Debug.WriteLine("SpellCheckHelper.Ignore: " + ex.Message); }
        }
    }
}
