from pathlib import Path

import bpy

project_root = Path(__file__).resolve().parents[1]
fbx_path = project_root / "Assets" / "people" / "1" / "Meshy_AI_11_biped_Animation_Walking_withSkin.fbx"
report_path = project_root / "Tools" / "duck_head_projection_report.txt"

bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete()
bpy.ops.import_scene.fbx(filepath=str(fbx_path))

mesh_obj = next(obj for obj in bpy.context.scene.objects if obj.type == "MESH")
armature = next(obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE")
mesh = mesh_obj.data


def dominant_group_name(vertex_index):
    vertex = mesh.vertices[vertex_index]
    if not vertex.groups:
        return ""
    group = max(vertex.groups, key=lambda item: item.weight)
    return mesh_obj.vertex_groups[group.group].name


def dominant_polygon_group(polygon):
    names = [dominant_group_name(index) for index in polygon.vertices]
    return max(set(names), key=names.count)


head_bone = armature.data.bones.get("Head")
front_bone = armature.data.bones.get("headfront")
direction = (front_bone.head_local - head_bone.head_local).normalized()

projections = []
for polygon in mesh.polygons:
    if dominant_polygon_group(polygon) != "Head":
        continue

    center = sum((mesh.vertices[index].co for index in polygon.vertices), mesh.vertices[polygon.vertices[0]].co * 0)
    center /= len(polygon.vertices)
    projections.append(center.dot(direction))

projections.sort()
lines = [
    f"Head polygons: {len(projections)}",
    f"Direction: {direction.x:.5f}, {direction.y:.5f}, {direction.z:.5f}",
]
for percent in (50, 70, 80, 85, 90, 92, 95, 97, 99):
    index = min(len(projections) - 1, int(len(projections) * percent / 100))
    lines.append(f"p{percent}: {projections[index]:.5f}")

report_path.write_text("\n".join(lines) + "\n", encoding="utf-8")
