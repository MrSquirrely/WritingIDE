using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

using WritingIDE.Core.Grammar;

namespace WritingIDE.Services;

/// <summary>Spell checking through the Windows Spell Checking API (ISpellCheckerFactory, Windows 8+).</summary>
/// <remarks>All calls are serialised with a lock; the COM object is created lazily on the first (background) call.</remarks>
public sealed class WindowsSpellingProvider(string languageTag = "en-US") : ISpellingProvider {
    private static readonly Guid SpellCheckerFactoryClsid = new("7AB36653-1796-484B-BDFA-E74F1DB7C1DC");

    private readonly Lock _lock = new();
    private ISpellChecker? _checker;
    private bool _initialized;

    public bool IsAvailable { get; private set; } = true;

    public string? InitializationError { get; private set; }

    public IReadOnlyList<SpellingIssue> Check(string text) {
        lock (_lock) {
            ISpellChecker? checker = GetChecker();
            if ((checker is null) || (text.Length == 0)) {
                return [];
            }
            List<SpellingIssue> issues = [];
            IEnumSpellingError? errors = null;
            try {
                errors = checker.Check(text);
                while (errors.Next(out ISpellingError? error) == 0 && error is not null) {
                    try {
                        CorrectiveAction action = error.GetCorrectiveAction();
                        // Repeated words are reported by the grammar engine's own rule.
                        if (action != CorrectiveAction.Delete) {
                            string? replacement = action == CorrectiveAction.Replace ? error.GetReplacement() : null;
                            issues.Add(new SpellingIssue((int)error.GetStartIndex(), (int)error.GetLength(), replacement));
                        }
                    }
                    finally {
                        Marshal.ReleaseComObject(error);
                    }
                }
            }
            catch (COMException) {
                // A failing check should never break analysis; report nothing for this run.
            }
            finally {
                if (errors is not null) {
                    Marshal.ReleaseComObject(errors);
                }
            }
            return issues;
        }
    }

    public IReadOnlyList<string> Suggest(string word) {
        lock (_lock) {
            ISpellChecker? checker = GetChecker();
            if (checker is null) {
                return [];
            }
            IEnumString? suggestions = null;
            try {
                suggestions = checker.Suggest(word);
                return Enumerate(suggestions).Take(8).ToList();
            }
            catch (COMException) {
                return [];
            }
            finally {
                // Each call returns a new COM enumerator; release it rather than waiting for the finalizer.
                if (suggestions is not null) {
                    Marshal.ReleaseComObject(suggestions);
                }
            }
        }
    }

    public bool IsCorrect(string word) {
        return Check(word).Count == 0;
    }

    private ISpellChecker? GetChecker() {
        if (_initialized) {
            return _checker;
        }
        _initialized = true;
        try {
            Type factoryType = Type.GetTypeFromCLSID(SpellCheckerFactoryClsid, throwOnError: true)!;
            ISpellCheckerFactory factory = (ISpellCheckerFactory)Activator.CreateInstance(factoryType)!;
            string language = factory.IsSupported(languageTag) != 0 ? languageTag : "en-US";
            _checker = factory.CreateSpellChecker(language);
        }
        catch (Exception exception) {
            IsAvailable = false;
            InitializationError = exception.Message;
            _checker = null;
        }
        return _checker;
    }

    private static IEnumerable<string> Enumerate(IEnumString enumString) {
        string[] buffer = new string[1];
        while (enumString.Next(1, buffer, IntPtr.Zero) == 0) {
            yield return buffer[0];
        }
    }

    private enum CorrectiveAction {
        None = 0,
        GetSuggestions = 1,
        Replace = 2,
        Delete = 3
    }

    [ComImport]
    [Guid("8E018A9D-2415-4677-BF08-794EA61F94BB")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpellCheckerFactory {
        IEnumString GetSupportedLanguages();

        int IsSupported([MarshalAs(UnmanagedType.LPWStr)] string languageTag);

        ISpellChecker CreateSpellChecker([MarshalAs(UnmanagedType.LPWStr)] string languageTag);
    }

    [ComImport]
    [Guid("B6FD0B71-E2BC-4653-8D05-F197E412770B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpellChecker {
        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetLanguageTag();

        IEnumSpellingError Check([MarshalAs(UnmanagedType.LPWStr)] string text);

        IEnumString Suggest([MarshalAs(UnmanagedType.LPWStr)] string word);

        void Add([MarshalAs(UnmanagedType.LPWStr)] string word);

        void Ignore([MarshalAs(UnmanagedType.LPWStr)] string word);

        void AutoCorrect([MarshalAs(UnmanagedType.LPWStr)] string from, [MarshalAs(UnmanagedType.LPWStr)] string to);

        byte GetOptionValue([MarshalAs(UnmanagedType.LPWStr)] string optionId);

        IEnumString GetOptionIds();

        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetId();

        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetLocalizedName();

        uint AddSpellCheckerChanged(IntPtr handler);

        void RemoveSpellCheckerChanged(uint eventCookie);

        IntPtr GetOptionDescription([MarshalAs(UnmanagedType.LPWStr)] string optionId);

        IEnumSpellingError ComprehensiveCheck([MarshalAs(UnmanagedType.LPWStr)] string text);
    }

    [ComImport]
    [Guid("803E3BD4-2828-4410-8290-418D1D73C762")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumSpellingError {
        [PreserveSig]
        int Next(out ISpellingError? value);
    }

    [ComImport]
    [Guid("B7C82D61-FBE8-4B47-9B27-6C0D2E0DE0A3")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpellingError {
        uint GetStartIndex();

        uint GetLength();

        CorrectiveAction GetCorrectiveAction();

        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetReplacement();
    }
}