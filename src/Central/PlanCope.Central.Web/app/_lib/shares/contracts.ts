export type PublicShareMetric = { value: number | null; status: "available" | "suppressed" | "unavailable" };
export interface StatsShareCreateRequest {
  groupBy: "locality" | "department" | "course" | "subject" | "year";
  filters: Record<string, string>;
  metrics?: Array<"attemptCount" | "weightedScorePercent">;
  expiresAt?: string | null;
}
export interface StatsShareCreated { id: string; url: string; expiresAt: string }
export interface PublicStatsShare {
  groupBy: string;
  groupLabel: string;
  filters: Record<string, string>;
  rows: Array<{ label: string; attemptCount: PublicShareMetric; weightedScorePercent: PublicShareMetric }>;
  totalAttempts: PublicShareMetric;
  totalWeightedScorePercent: PublicShareMetric;
  generatedAt: string;
  expiresAt: string;
}
