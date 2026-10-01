from pathlib import Path

import bpy

project_root = Path(__file__).resolve().parents[1]
source_fbx = project_root / "Assets" / "people" / "1" / "Meshy_AI_11_biped_Animation_Walking_withSkin.fbx"
output_dir = project_root / "Assets" / "people" / "1" / "SeparatedMaterials"
output_fbx = output_dir / "Duck1_SeparatedMaterials.fbx"
report_path = project_root / "Tools" / "duck_material_separation_report.txt"

FOOT_GROUP_MARKERS = ("Foot", "Toe")
BEAK_PERCENTILE = 0.92

output_dir.mkdir(parents=True, exist_ok=True)

bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete()
bpy.ops.import_scene.fbx(filepath=str(source_fbx))

mesh_obj = next(obj for obj in bpy.context.scene.objects if obj.type == "MESH")
armature = next(obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE")
mesh = mesh_obj.data
source_material = mesh_obj.material_slots[0].material if mesh_obj.material_slots else None


def copy_material(name, color):
    material = source_material.copy() if source_material is not None else bpy.data.materials.new(name)
    material.name = name
    material.diffuse_color = color
    return material


materials = {
    "Body": copy_material("Body_Recolor", (1.0, 0.92, 0.72, 1.0)),
    "Beak": copy_material("Beak", (1.0, 0.72, 0.18, 1.0)),
    "Feet": copy_material("Feet", (1.0, 0.55, 0.12, 1.0)),
}

mesh.materials.clear()
for material in materials.values():
    mesh.materials.append(material)

material_indices = {name: index for index, name in enumerate(materials)}


def dominant_group_name(vertex_index):
    vertex = mesh.vertices[vertex_index]
    if not vertex.groups:
        return ""
    group = max(vertex.groups, key=lambda item: item.weight)
    return mesh_obj.vertex_groups[group.group].name


def dominant_polygon_group(polygon):
    names = [dominant_group_name(index) for index in polygon.vertices]
    return max(set(names), key=names.count)


def polygon_center(polygon):
    center = mesh.vertices[polygon.vertices[0]].co.copy()
    for vertex_index in polygon.vertices[1:]:
        center += mesh.vertices[vertex_index].co
    return center / len(polygon.vertices)


head_bone = armature.data.bones.get("Head")
front_bone = armature.data.bones.get("headfront")
beak_direction = (front_bone.head_local - head_bone.head_local).normalized()

head_projections = []
for polygon in mesh.polygons:
    if dominant_polygon_group(polygon) == "Head":
        head_projections.append(polygon_center(polygon).dot(beak_direction))

head_projections.sort()
threshold_index = min(len(head_projections) - 1, int(len(head_projections) * BEAK_PERCENTILE))
beak_projection_threshold = head_projections[threshold_index]

counts = {"Body": 0, "Beak": 0, "Feet": 0}
for polygon in mesh.polygons:
    group_name = dominant_polygon_group(polygon)

    if any(marker in group_name for marker in FOOT_GROUP_MARKERS):
        category = "Feet"
    elif group_name == "Head" and polygon_center(polygon).dot(beak_direction) >= beak_projection_threshold:
        category = "Beak"
    else:
        category = "Body"

    polygon.material_index = material_indices[category]
    counts[category] += 1

for obj in bpy.context.scene.objects:
    obj.select_set(True)

bpy.context.view_layer.objects.active = mesh_obj
bpy.ops.export_scene.fbx(
    filepath=str(output_fbx),
    use_selection=True,
    add_leaf_bones=False,
    bake_anim=False,
    path_mode="AUTO",
)

report_path.write_text(
    "\n".join(
        [
            f"Source: {source_fbx}",
            f"Output: {output_fbx}",
            f"Beak projection threshold: {beak_projection_threshold:.5f}",
            "Polygon material assignment:",
            f"  Body: {counts['Body']}",
            f"  Beak: {counts['Beak']}",
            f"  Feet: {counts['Feet']}",
        ]
    )
    + "\n",
    encoding="utf-8",
)
