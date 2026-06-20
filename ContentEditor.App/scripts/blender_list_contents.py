import bpy
import json

def get_object_materials(obj):
    mats = []
    for slot in obj.material_slots:
        if slot.material:
            mats.append(slot.material.name)
    return mats


scene_info = {
    'armatures': [],
    'standalone_objects': [],
    'materials': sorted({mat.name for mat in bpy.data.materials}),
}

# Build armature list
for armature in [obj for obj in bpy.data.objects if obj.type == 'ARMATURE']:
    users = []

    for obj in bpy.data.objects:
        if obj == armature:
            continue

        uses_armature = False

        # Direct armature parenting
        if obj.parent == armature:
            uses_armature = True

        # Armature modifier
        if not uses_armature:
            for mod in obj.modifiers:
                if mod.type == 'ARMATURE' and mod.object == armature:
                    uses_armature = True
                    break

        if uses_armature:
            users.append({
                'name': obj.name,
                'type': obj.type,
                'materials': get_object_materials(obj),
            })

    scene_info['armatures'].append({
        'name': armature.name,
        'objects': users,
    })


# Objects not parented to an armature
for obj in bpy.data.objects:
    if obj.type == 'ARMATURE':
        continue

    parent_is_armature = (
        obj.parent is not None and
        obj.parent.type == 'ARMATURE'
    )

    if not parent_is_armature:
        scene_info['standalone_objects'].append({
            'name': obj.name,
            'type': obj.type,
            'materials': get_object_materials(obj),
        })


# Print a single JSON object for the launcher to consume
print(
    'SCENE_INFO_BEGIN',
    json.dumps(scene_info, indent=None, separators=(',', ':')),
    'SCENE_INFO_END'
)
