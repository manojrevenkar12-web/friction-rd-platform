"""Console screenshots for the README. Needs the API on :5080 (RDO_DEMO_SEED=60), the analytics
service on :8100 and the console preview on :4173.

python scripts/screenshots.py [--chrome /usr/bin/google-chrome]
"""

from __future__ import annotations

import argparse
from pathlib import Path

from playwright.sync_api import sync_playwright

OUT = Path(__file__).parents[1] / "docs" / "screenshots"


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--chrome", default="/usr/bin/google-chrome")
    ap.add_argument("--url", default="http://localhost:4173")
    a = ap.parse_args()
    OUT.mkdir(parents=True, exist_ok=True)
    with sync_playwright() as p:
        b = p.chromium.launch(executable_path=a.chrome)
        page = b.new_page(viewport={"width": 1440, "height": 960}, reduced_motion="reduce")
        page.goto(f"{a.url}/#requests", wait_until="networkidle")
        page.wait_for_selector(".request-detail")
        asked = page.locator(".chip.needsClarification").first
        if asked.count():
            asked.click()
            page.wait_for_timeout(300)
            page.screenshot(path=str(OUT / "requests-asked.png"))
        page.locator(".chip.accepted").first.click()
        page.wait_for_timeout(300)
        page.screenshot(path=str(OUT / "requests.png"))
        page.click("text=Bench plan")
        page.wait_for_selector(".gantt-row")
        page.wait_for_timeout(500)
        page.screenshot(path=str(OUT / "plan.png"))
        page.click("text=First come, first served")
        page.wait_for_timeout(800)
        page.screenshot(path=str(OUT / "plan-fifo.png"))
        page.click("text=Run monitor")
        page.wait_for_selector(".verdict")
        page.wait_for_timeout(500)
        page.screenshot(path=str(OUT / "monitor.png"))
        page.click("text=Pad cracked")
        page.wait_for_selector(".verdict")
        page.wait_for_timeout(500)
        page.screenshot(path=str(OUT / "monitor-cracked.png"))
        page.click("text=Study")
        page.wait_for_selector(".designs")
        page.screenshot(path=str(OUT / "study.png"), full_page=True)
        m = b.new_page(viewport={"width": 390, "height": 844}, device_scale_factor=2, reduced_motion="reduce")
        m.goto(f"{a.url}/#monitor", wait_until="networkidle")
        m.wait_for_selector(".verdict")
        m.screenshot(path=str(OUT / "monitor-mobile.png"), full_page=True)
        b.close()


if __name__ == "__main__":
    main()
