layout (std140) uniform GlobalData
{
    mat4 uView;
    mat4 uProjection;
    mat4 uProjectionView;
    vec3 uCameraPosition;
};
