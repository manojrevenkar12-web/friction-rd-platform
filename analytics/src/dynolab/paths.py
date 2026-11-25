from __future__ import annotations

import os
from pathlib import Path

#: The repository root (config/, reports/, src/). A container sets DYNOLAB_ROOT.
ROOT = Path(os.environ.get("DYNOLAB_ROOT", Path(__file__).resolve().parents[3]))
