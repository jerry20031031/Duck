from collections import Counter
from pathlib import Path

import bpy

project_root = Path(__file__).resolve().parents[1]
fbx_path = project_root / "Assets" / "people" / "1" / "Meshy_AI_11_biped_Animation_Walking_withSkin.fbx"
report_path = project_root / "Tools" / "duck_vertex_groups_report.txt"

bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete()
bpy.ops.import_scene.fbx(filepath=str(fbx_path))

mesh_obj = next(obj for obj in bpy.context.scene.objects if obj.type == "MESH")
mesh = mesh_obj.data

def dominant_group_name(vertex_index):
    vertex = mesh.vertices[vertex_index]
    if not vertex.groups:
        return "<none>"

    group = max(vertex.groups, key=lambda item: item.weight)
    return mesh_obj.vertex_groups[group.group].name

vertex_counts = Counter()
for vertex in mesh.vertices:
    if not vertex.groups:
        vertex_counts["<none>"] += 1
        continue
    group = max(vertex.groups, key=lambda item: item.weight)
    vertex_counts[mesh_obj.vertex_groups[group.group].name] += 1

polygon_counts = Counter()
for polygon in mesh.polygons:
    names = [dominant_group_name(index) for index in polygon.vertices]
    polygon_counts[Counter(names).most_common(1)[0][0]] += 1

lines = ["Dominant vertex groups by vertex:"]
for name, count in vertex_counts.most_common():
    lines.append(f"  {name}: {count}")

lines.append("")
lines.append("Dominant vertex groups by polygon:")
for name, count in polygon_counts.most_common():
    lines.append(f"  {name}: {count}")

report_path.write_text("\n".join(lines) + "\n", encoding="utf-8")
