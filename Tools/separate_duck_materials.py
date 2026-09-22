from collections import Counter
from pathlib import Path

import bpy

project_root = Path(__file__).resolve().parents[1]
source_fbx = project_root / "Assets" / "people" / "1" / "Meshy_AI_11_biped_Animation_Walking_withSkin.fbx"
output_dir = project_root / "Assets" / "people" / "1" / "SeparatedMaterials"
output_fbx = output_dir / "Duck1_SeparatedMaterials.fbx"
report_path = project_root / "Tools" / "duck_material_separation_report.txt"

FOOT_GROUP_MARKERS = ("Foot", "Toe")
HEAD_GROUP_MARKERS = ("Head", "neck")

output_dir.mkdir(parents=True, exist_ok=True)

bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete()
bpy.ops.import_scene.fbx(filepath=str(source_fbx))

mesh_obj = next(obj for obj in bpy.context.scene.objects if obj.type == "MESH")
mesh = mesh_obj.data
source_material = mesh_obj.material_slots[0].material if mesh_obj.material_slots else None


def copy_material(name, color):
    if source_material is not None:
        material = source_material.copy()
    else:
        material = bpy.data.materials.new(name)

    material.name = name
    material.diffuse_color = color
    return material


materials = {
    "Body": copy_material("Body_Recolor", (1.0, 0.92, 0.72, 1.0)),
    "Beak": copy_material("Beak", (1.0, 0.72, 0.18, 1.0)),
    "Feet": copy_material("Feet", (1.0, 0.55, 0.12, 1.0)),
    "Other": copy_material("Other", (1.0, 1.0, 1.0, 1.0)),
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


def polygon_group_names(polygon):
    return [dominant_group_name(index) for index in polygon.vertices]


def has_group_marker(group_names, markers):
    return any(marker in name for name in group_names for marker in markers)


def find_base_color_image():
    if source_material is None or not source_material.use_nodes:
        return None

    for node in source_material.node_tree.nodes:
        if node.type == "TEX_IMAGE" and node.image is not None:
            return node.image

    return None


base_image = find_base_color_image()
if base_image is not None:
    base_image.pixels[0]


def sample_image(uv):
    if base_image is None or not mesh.uv_layers:
        return (1.0, 1.0, 1.0, 1.0)

    width, height = base_image.size
    x = max(0, min(width - 1, int((uv.x % 1.0) * width)))
    y = max(0, min(height - 1, int((uv.y % 1.0) * height)))
    offset = (y * width + x) * 4
    pixels = base_image.pixels
    return (pixels[offset], pixels[offset + 1], pixels[offset + 2], pixels[offset + 3])


def polygon_average_color(polygon):
    if base_image is None or not mesh.uv_layers:
        return (1.0, 1.0, 1.0, 1.0)

    uv_layer = mesh.uv_layers.active.data
    colors = [sample_image(uv_layer[loop_index].uv) for loop_index in polygon.loop_indices]
    count = len(colors)
    return (
        sum(color[0] for color in colors) / count,
        sum(color[1] for color in colors) / count,
        sum(color[2] for color in colors) / count,
        sum(color[3] for color in colors) / count,
    )


def is_beak_color(color):
    red, green, blue, _ = color
    return red > 0.62 and green > 0.38 and blue < 0.32 and (red - blue) > 0.35


category_counts = Counter()
for polygon in mesh.polygons:
    group_names = polygon_group_names(polygon)
    color = polygon_average_color(polygon)

    if has_group_marker(group_names, FOOT_GROUP_MARKERS):
        category = "Feet"
    elif has_group_marker(group_names, HEAD_GROUP_MARKERS) and is_beak_color(color):
        category = "Beak"
    else:
        category = "Body"

    polygon.material_index = material_indices[category]
    category_counts[category] += 1

for obj in bpy.context.scene.objects:
    obj.select_set(True)

bpy.context.view_layer.objects.active = mesh_obj
bpy.ops.export_scene.fbx(
    filepath=str(output_fbx),
    use_selection=True,
    add_leaf_bones=False,
    bake_anim=True,
    path_mode="AUTO",
)

lines = [
    f"Source: {source_fbx}",
    f"Output: {output_fbx}",
    f"Texture sampled: {base_image.name if base_image else '<none>'}",
    "Polygon material assignment:",
]
for category, count in category_counts.most_common():
    lines.append(f"  {category}: {count}")

report_path.write_text("\n".join(lines) + "\n", encoding="utf-8")
