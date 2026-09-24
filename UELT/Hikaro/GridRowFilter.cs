namespace UELT.Hikaro;

public enum GridRowFilter
{
    None,
    Untranslated,
    Modified,
    NeedsReview,
    Approved,
    HideApproved,
    GroupByID,
    GroupByText,
    GroupByTranslation,
    SortByOriginalWordCount,
    SortByTranslationWordCount,
    SpellCheck,
    Glossary,
    TranslationMemory,
    TranslationMemoryUnmodified,
    CommonErrors
}