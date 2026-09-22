from pathlib import Path

import bpy

project_root = Path(__file__).resolve().parents[1]
fbx_path = project_root / "Assets" / "people" / "1" / "SeparatedMaterials" / "Duck1_SeparatedMaterials.fbx"
report_path = project_root / "Tools" / "duck_separated_fbx_report.txt"

bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete()

bpy.ops.import_scene.fbx(filepath=str(fbx_path))

lines = [f"Imported: {fbx_path}"]
for obj in bpy.context.scene.objects:
    lines.append(f"Object: {obj.name} type={obj.type}")
    if obj.type != "MESH":
        continue

    mesh = obj.data
    lines.append(f"  Mesh: {mesh.name}")
    lines.append(f"  Vertices: {len(mesh.vertices)}")
    lines.append(f"  Polygons: {len(mesh.polygons)}")
    lines.append(f"  Materials: {len(obj.material_slots)}")
    for index, slot in enumerate(obj.material_slots):
        material_name = slot.material.name if slot.material else "<none>"
        polygon_count = sum(1 for poly in mesh.polygons if poly.material_index == index)
        lines.append(f"    [{index}] {material_name}: {polygon_count} polygons")
    lines.append(f"  UV layers: {len(mesh.uv_layers)}")

report_path.write_text("\n".join(lines) + "\n", encoding="utf-8")
