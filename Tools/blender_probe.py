from pathlib import Path

import bpy

Path("blender_probe_result.txt").write_text(
    f"BLENDER_OK {bpy.app.version_string}\n",
    encoding="utf-8",
)
