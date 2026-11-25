import { render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { App } from "../app/App";
import { highlight } from "../features/requests/api";

afterEach(() => vi.restoreAllMocks());

describe("highlight", () => {
  it("marks the words each field was read from, in order, without overlaps", () => {
    const parts = highlight("bitte AK Master für FM 2231 auf Compact C1 VA bis KW 14", {
      formulation: { value: "FM-2231", evidence: "FM 2231" },
      application: { value: "A01", evidence: "Compact C1 VA" },
      procedure: { value: "AKM", evidence: "AK Master" },
      samples: { value: null, evidence: null },
      deadline: { value: "2027-04-09", evidence: "KW 14" },
      questions: [],
    });
    expect(parts.filter((p) => p.field).map((p) => p.field)).toEqual(["procedure", "formulation", "application", "deadline"]);
    expect(parts.map((p) => p.text).join("")).toBe("bitte AK Master für FM 2231 auf Compact C1 VA bis KW 14");
  });
});

describe("console states", () => {
  it("tells how to start the API when it is not reachable", async () => {
    vi.spyOn(globalThis, "fetch").mockRejectedValue(new TypeError("offline"));
    render(<App initial="requests" />);
    expect(await screen.findByText(/dotnet run --project src\/Rdo.Api/)).toBeInTheDocument();
  });

  it("says how to generate study results when they are missing", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response("", { status: 404 }));
    render(<App initial="study" />);
    expect(await screen.findByText(/dynolab lab dev/)).toBeInTheDocument();
  });

  it("shows an empty plan with what to do", async () => {
    vi.spyOn(globalThis, "fetch").mockImplementation(async (url) => {
      const u = String(url);
      const body = u.includes("schedule") ? { policy: "slack-aware", generatedAt: new Date().toISOString(), changeovers: 0, late: 0, unplanned: 0, dispatches: [] } : [];
      return new Response(JSON.stringify(body), { status: 200 });
    });
    render(<App initial="plan" />);
    expect(await screen.findByText("Nothing to plan")).toBeInTheDocument();
  });
});
