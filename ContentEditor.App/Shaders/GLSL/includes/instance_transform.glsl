#ifdef ENABLE_INSTANCING
    mat4 transform = instancedMatrix;
#else
    mat4 transform = uModel;
#endif
