import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { UpdateStatus } from "./UpdateStatus";

describe("UpdateStatus", () => {
  it("renders the installed version string passed in", () => {
    const html = renderToStaticMarkup(<UpdateStatus appVersion="1.2.3" onCheckForUpdates={() => undefined} />);
    expect(html).toContain("1.2.3");
  });

  it("renders the check for updates button", () => {
    const html = renderToStaticMarkup(<UpdateStatus appVersion="1.2.3" onCheckForUpdates={() => undefined} />);
    expect(html).toContain("Check for updates");
  });

  it("disables the button while checking for updates", () => {
    const html = renderToStaticMarkup(
      <UpdateStatus appVersion="1.2.3" onCheckForUpdates={() => undefined} isChecking />
    );
    expect(html).toContain('disabled="');
  });
});