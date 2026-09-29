/**
 * Mirrors `SuggestX.Contracts.Dtos.SuggestionItem`/`SuggestionResponse`
 * (src/shared/SuggestX.Contracts/Dtos/SuggestionDtos.cs). ASP.NET's default
 * JSON casing (camelCase) is what actually arrives over the wire, which is
 * why these fields don't match the C# records' PascalCase property names.
 */
export interface SuggestionItem {
  phrase: string;
  frequency: number;
}

export interface SuggestionResponse {
  prefix: string;
  suggestions: SuggestionItem[];
  /** Which phrases in `suggestions` were reordered ahead of their global ranking because they matched the caller's own recent-search list (Phase 7 personalization). */
  personalizedPhrases: string[];
}
