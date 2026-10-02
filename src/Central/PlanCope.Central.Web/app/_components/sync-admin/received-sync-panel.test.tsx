import { createElement } from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { ReceivedSyncPanel } from "./received-sync-panel";
import type { ReceivedSyncPage } from "../../_lib/api/server";

const page: ReceivedSyncPage = {
  page: 1,
  pageSize: 50,
  totalCount: 1,
  inboxProcessing: {
    pending: 1,
    failed: 0,
    latestDurableReceivedAt: "2026-10-02T12:00:00Z",
    nextRetryAt: null
  },
  items: [{
    attemptId: "attempt-1",
    receivedAt: "2026-10-02T12:00:00Z",
    receiptStatus: "durable",
    durableReceivedAt: "2026-10-02T12:00:00Z",
    processingStatus: "retrying",
    processingUpdatedAt: null,
    rollupStatus: "pending",
    nodeId: "node-1",
    cue: "180055400",
    schoolYear: "2026",
    rosterSectionId: "section-1",
    examVersionId: "version-1",
    gradingStatus: "pending",
    gradingReason: null,
    attributionStatus: "pending",
    attributionReason: null
  }]
};

describe("ReceivedSyncPanel", () => {
  it("keeps durable receipt, downstream processing, and rollup as separate states", () => {
    const html = renderToStaticMarkup(createElement(ReceivedSyncPanel, { initialPage: page }));

    expect(html).toContain("Recepción durable");
    expect(html).toContain("Procesamiento posterior");
    expect(html).toContain("Reintento pendiente");
    expect(html).toContain("Rollup");
    expect(html).toContain("Pendiente");
  });
});
