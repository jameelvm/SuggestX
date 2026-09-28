import { browserApiFetch } from "@/lib/api/browser-client";
import type {
  AggregatorStatus,
  CollectionStatus,
  SuggestionServiceStatus,
  TrieBuilderStatus,
  TrieTreeResponse,
} from "@/types/insights";

/**
 * Every one of these is a `/_debug/*` endpoint the backend already built
 * for its own module-by-module verification throughout this project — the
 * insights panel's whole job is surfacing that existing visibility to a
 * person watching a browser, not building new instrumentation. Collection
 * and Suggestion routes already existed for Modules 1-2; Aggregator and
 * TrieBuilder needed new Gateway routes added specifically for this
 * module (see docker-compose.yml / Gateway appsettings.json).
 */
export function fetchCollectionStatus(): Promise<CollectionStatus> {
  return browserApiFetch<CollectionStatus>("/search-events/_debug/status");
}

export function fetchAggregatorStatus(): Promise<AggregatorStatus> {
  return browserApiFetch<AggregatorStatus>("/aggregator/_debug/status");
}

export function fetchTrieBuilderStatus(): Promise<TrieBuilderStatus> {
  return browserApiFetch<TrieBuilderStatus>("/trie-builder/_debug/status");
}

export function fetchSuggestionServiceStatus(): Promise<SuggestionServiceStatus> {
  return browserApiFetch<SuggestionServiceStatus>("/suggestions/_debug/status");
}

export function fetchTrieTree(): Promise<TrieTreeResponse> {
  return browserApiFetch<TrieTreeResponse>("/trie-builder/_debug/tree");
}
