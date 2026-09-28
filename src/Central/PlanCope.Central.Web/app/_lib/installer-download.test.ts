import { describe, it, expect } from "vitest";
import { buildInstallerDownloadHref } from "./installer-download";

describe("buildInstallerDownloadHref", () => {
  it("builds the same-origin download route for a channel", () => {
    expect(buildInstallerDownloadHref("stable")).toBe("/api/installer/download?channel=stable");
  });

  it("URL-encodes the channel", () => {
    expect(buildInstallerDownloadHref("beta test")).toBe("/api/installer/download?channel=beta%20test");
  });
});
