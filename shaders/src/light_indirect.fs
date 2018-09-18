//------------------------------------------------------------------------------
// Image based lighting configuration
//------------------------------------------------------------------------------

#ifndef TARGET_MOBILE
#define IBL_SPECULAR_OCCLUSION
#define IBL_OFF_SPECULAR_PEAK
#endif

// Number of spherical harmonics bands (1, 2 or 3)
#if defined(TARGET_MOBILE)
#define SPHERICAL_HARMONICS_BANDS           2
#else
#define SPHERICAL_HARMONICS_BANDS           3
#endif

// Diffuse reflectance
#define IBL_IRRADIANCE_SPHERICAL_HARMONICS  0

// Specular reflectance
#define IBL_PREFILTERED_DFG_LUT             0

#define IBL_IRRADIANCE                      IBL_IRRADIANCE_SPHERICAL_HARMONICS
#define IBL_PREFILTERED_DFG                 IBL_PREFILTERED_DFG_LUT

// Cloth DFG approximation
#define CLOTH_DFG_ASHIKHMIN                 0
#define CLOTH_DFG_CHARLIE                   1

#define CLOTH_DFG                           CLOTH_DFG_CHARLIE

// IBL integration algorithm
#define IBL_INTEGRATION_PREFILTERED_CUBEMAP         0
#define IBL_INTEGRATION_IMPORTANCE_SAMPLING         1
#define IBL_INTEGRATION_IMPORTANCE_SAMPLING_COUNT   32
#define IBL_INTEGRATION                             IBL_INTEGRATION_PREFILTERED_CUBEMAP

//------------------------------------------------------------------------------
// IBL utilities
//------------------------------------------------------------------------------

vec3 decodeDataForIBL(const vec4 data) {
#if defined(IBL_USE_RGBM)
    return decodeRGBM(data);
#else
    return data.rgb;
#endif
}

//------------------------------------------------------------------------------
// IBL prefiltered DFG term implementations
//------------------------------------------------------------------------------

vec2 PrefilteredDFG_LUT(float coord, float NoV) {
    // coord = sqrt(linear_roughness), which is the mapping used by cmgen.
    return textureLod(light_iblDFG, vec2(NoV, coord), 0.0).rg;
}

#if CLOTH_DFG == CLOTH_DFG_ASHIKHMIN
/**
 * Analytical approximation of the pre-filtered DFG terms for the cloth shading
 * model. This approximation is based on the Ashikhmin distribution term and
 * the Neubelt visibility term. See brdf.fs for more details.
 */
vec2 PrefilteredDFG_Cloth_Ashikhmin(float roughness, float NoV) {
    const vec4 c0 = vec4(0.24,  0.93, 0.01, 0.20);
    const vec4 c1 = vec4(2.00, -1.30, 0.40, 0.03);

    float s = 1.0 - NoV;
    float e = s - c0.y;
    float g = c0.x * exp2(-(e * e) / (2.0 * c0.z)) + s * c0.w;
    float n = roughness * c1.x + c1.y;
    float r = max(1.0 - n * n, c1.z) * g;

    return vec2(r, r * c1.w);
}
#endif

 #if CLOTH_DFG == CLOTH_DFG_CHARLIE
/**
 * Analytical approximation of the pre-filtered DFG terms for the cloth shading
 * model. This approximation is based on the Estevez & Kulla distribution term
 * ("Charlie" sheen) and the Neubelt visibility term. See brdf.fs for more
 * details.
 */
vec2 PrefilteredDFG_Cloth_Charlie(float roughness, float NoV) {
    const vec3 c0 = vec3(0.95, 1250.0, 0.0095);
    const vec4 c1 = vec4(0.04, 0.2, 0.3, 0.2);

    float a = 1.0 - NoV;
    float b = 1.0 - roughness;

    float n = pow(c1.x + a, 64.0);
    float e = b - c0.x;
    float g = exp2(-(e * e) * c0.y);
    float f = b + c1.y;
    float a2 = a * a;
    float a3 = a2 * a;
    float c = n * g + c1.z * (a + c1.w) * roughness + f * f * a3 * a3 * a2;
    float r = min(c, 18.0);

    return vec2(r, r * c0.z);
}
#endif

//------------------------------------------------------------------------------
// IBL environment BRDF dispatch
//------------------------------------------------------------------------------

vec2 prefilteredDFG(float roughness, float NoV) {
#if defined(SHADING_MODEL_CLOTH)
    #if CLOTH_DFG == CLOTH_DFG_ASHIKHMIN
        return PrefilteredDFG_Cloth_Ashikhmin(roughness, NoV);
    #elif CLOTH_DFG == CLOTH_DFG_CHARLIE
        return PrefilteredDFG_Cloth_Charlie(roughness, NoV);
    #endif
#else
    #if IBL_PREFILTERED_DFG == IBL_PREFILTERED_DFG_LUT
        // PrefilteredDFG_LUT() takes a coordinate, which is sqrt(linear_roughness) = roughness
        return PrefilteredDFG_LUT(roughness, NoV);
    #endif
#endif
}

//------------------------------------------------------------------------------
// IBL irradiance implementations
//------------------------------------------------------------------------------

vec3 Irradiance_SphericalHarmonics(const vec3 n) {
    return max(
          frameUniforms.iblSH[0]
#if SPHERICAL_HARMONICS_BANDS >= 2
        + frameUniforms.iblSH[1] * (n.y)
        + frameUniforms.iblSH[2] * (n.z)
        + frameUniforms.iblSH[3] * (n.x)
#endif
#if SPHERICAL_HARMONICS_BANDS >= 3
        + frameUniforms.iblSH[4] * (n.y * n.x)
        + frameUniforms.iblSH[5] * (n.y * n.z)
        + frameUniforms.iblSH[6] * (3.0 * n.z * n.z - 1.0)
        + frameUniforms.iblSH[7] * (n.z * n.x)
        + frameUniforms.iblSH[8] * (n.x * n.x - n.y * n.y)
#endif
        , 0.0);
}

//------------------------------------------------------------------------------
// IBL irradiance dispatch
//------------------------------------------------------------------------------

vec3 diffuseIrradiance(const vec3 n) {
#if IBL_IRRADIANCE == IBL_IRRADIANCE_SPHERICAL_HARMONICS
    return Irradiance_SphericalHarmonics(n);
#endif
}

//------------------------------------------------------------------------------
// IBL specular
//------------------------------------------------------------------------------

vec3 specularIrradiance(const vec3 r, float roughness) {
    // lod = lod_count * sqrt(linear_roughness), which is the mapping used by cmgen
    // where linear_roughness = roughness^2
    // using all the mip levels requires seamless cubemap sampling
    float lod = IBL_MAX_MIP_LEVEL * roughness;
    return decodeDataForIBL(textureLod(light_iblSpecular, r, lod));
}

vec3 specularIrradiance(const vec3 r, float roughness, float offset) {
    float lod = IBL_MAX_MIP_LEVEL * roughness * roughness;
    return decodeDataForIBL(textureLod(light_iblSpecular, r, lod + offset));
}

vec3 getSpecularDominantDirection(vec3 n, vec3 r, float linearRoughness) {
#if defined(IBL_OFF_SPECULAR_PEAK)
    float s = 1.0 - linearRoughness;
    return mix(n, r, s * (sqrt(s) + linearRoughness));
#else
    return r;
#endif
}

vec3 specularDFG(const PixelParams pixel) {
#if defined(SHADING_MODEL_CLOTH) || !defined(USE_MULTIPLE_SCATTERING_COMPENSATION)
    return pixel.f0 * pixel.dfg.x + pixel.dfg.y;
#else
    return mix(pixel.dfg.xxx, pixel.dfg.yyy, pixel.f0);
#endif
}

/**
 * Returns the reflected vector at the current shading point. The reflected vector
 * return by this function might be different from shading_reflected:
 * - For anisotropic material, we bend the reflection vector to simulate
 *   anisotropic indirect lighting
 * - The reflected vector may be modified to point towards the dominant specular
 *   direction to match reference renderings when the roughness increases
 */

vec3 getReflectedVector(const PixelParams pixel, const vec3 v, const vec3 n) {
#if defined(MATERIAL_HAS_ANISOTROPY)
    vec3  anisotropyDirection = pixel.anisotropy >= 0.0 ? pixel.anisotropicB : pixel.anisotropicT;
    vec3  anisotropicTangent  = cross(anisotropyDirection, v);
    vec3  anisotropicNormal   = cross(anisotropicTangent, anisotropyDirection);
    float bendFactor          = abs(pixel.anisotropy) * saturate(5.0 * pixel.roughness);
    vec3  bentNormal          = normalize(mix(n, anisotropicNormal, bendFactor));

    vec3 r = reflect(-v, bentNormal);
#else
    vec3 r = reflect(-v, n);
#endif
    return r;
}

vec3 getReflectedVector(const PixelParams pixel, const vec3 n) {
#if defined(MATERIAL_HAS_ANISOTROPY)
    vec3 r = getReflectedVector(pixel, shading_view, n);
#else
    vec3 r = shading_reflected;
#endif
    return getSpecularDominantDirection(n, r, pixel.linearRoughness);
}

//------------------------------------------------------------------------------
// Prefiltered importance sampling
//------------------------------------------------------------------------------

#if IBL_INTEGRATION == IBL_INTEGRATION_IMPORTANCE_SAMPLING
vec3 isEvaluateIBL(const PixelParams pixel, vec3 n, vec3 v, float NoV) {
    // TODO: for a true anisotropic BRDF, we need a real tangent space
    vec3 up = abs(n.z) < 0.999 ? vec3(0.0, 0.0, 1.0) : vec3(1.0, 0.0, 0.0);

    mat3 tangentToWorld;
    tangentToWorld[0] = normalize(cross(up, n));
    tangentToWorld[1] = cross(n, tangentToWorld[0]);
    tangentToWorld[2] = n;

    float linearRoughness = pixel.linearRoughness;
    float a2 = linearRoughness * linearRoughness;

    const float dim = float(1 << uint(IBL_MAX_MIP_LEVEL));
    const float omegaP = (4.0 * PI) / (6.0 * dim * dim);
    const float invOmegaP = 1.0 / omegaP;
    const float K = 4.0;

    // IMPORTANT: Keep numSample = 1 << numSampleBits
    const uint numSamples = uint(IBL_INTEGRATION_IMPORTANCE_SAMPLING_COUNT);
    const uint numSampleBits = uint(log2(float(numSamples)));
    const float invNumSamples = 1.0 / float(numSamples);

    vec3 indirectSpecular = vec3(0.0);
    for (uint i = 0u; i < numSamples; i++) {
        // Compute Hammersley sequence
        // TODO: these should come from uniforms
        // TODO: we should do this with logical bit operations
        uint t = i;
        uint bits = 0u;
        for (uint j = 0u; j < numSampleBits; j++) {
            bits = bits * 2u + (t - (2u * (t / 2u)));
            t /= 2u;
        }
        vec2 u = vec2(float(i), float(bits)) * invNumSamples;

        // Importance sampling D_GGX
        float phi = 2.0 * PI * u.x;
        float cosTheta2 = (1.0 - u.y) / (1.0 + (a2 - 1.0) * u.y);
        float cosTheta = sqrt(cosTheta2);
        float sinTheta = sqrt(1.0 - cosTheta2);

        vec3 h = tangentToWorld * vec3(cos(phi) * sinTheta, sin(phi) * sinTheta, cosTheta);

        // Since anisotropy doesn't work with prefiltering, we use the same "faux" anisotropy
        // we do when we use the prefiltered cubemap
        vec3 l = getReflectedVector(pixel, v, h);

        // Compute this sample's contribution to the brdf
        float NoL = dot(n, l);
        if (NoL > 0.0) {
            float LoH = max(dot(l, h), 0.0);
            float NoH = cosTheta;

            // PDF inverse (we must use D_GGX() here, which is used to generate samples)
            float ipdf = (4.0 * LoH) / (D_GGX(linearRoughness, NoH, h) * NoH);

            // See: "Real-time Shading with Filtered Importance Sampling", Jaroslav Krivanek
            // Prefiltering doesn't work with anisotropy
            float omegaS = invNumSamples * ipdf;
            float mipLevel = clamp(log2(K * omegaS * invOmegaP) * 0.5, 0.0, IBL_MAX_MIP_LEVEL);

            float D = distribution(linearRoughness, NoH, h);
            float V = visibility(pixel.roughness, linearRoughness, NoV, NoL, LoH);
            vec3  F = fresnel(pixel.f0, LoH);

            vec3 Fr = F * (D * V * ipdf * NoL);

            vec3 env = decodeDataForIBL(textureLod(light_iblSpecular, l, mipLevel));
            indirectSpecular += (Fr * env) * invNumSamples;
        }
    }

    return indirectSpecular;
}

void isEvaluateClearCoatIBL(const PixelParams pixel, float specularAO, inout vec3 Fd, inout vec3 Fr) {
#if defined(MATERIAL_HAS_CLEAR_COAT)
#if defined(MATERIAL_HAS_NORMAL) || defined(MATERIAL_HAS_CLEAR_COAT_NORMAL)
    // We want to use the geometric normal for the clear coat layer
    float clearCoatNoV = abs(dot(shading_clearCoatNormal, shading_view)) + FLT_EPS;
    vec3 clearCoatNormal = shading_clearCoatNormal;
#else
    float clearCoatNoV = shading_NoV;
    vec3 clearCoatNormal = shading_normal;
#endif
    // The clear coat layer assumes an IOR of 1.5 (4% reflectance)
    float Fc = F_Schlick(0.04, 1.0, clearCoatNoV) * pixel.clearCoat;
    float attenuation = 1.0 - Fc;
    Fd *= attenuation;
    Fr *= sq(attenuation);

    PixelParams p;
    p.roughness = pixel.clearCoatRoughness;
    p.f0 = vec3(0.04);
    p.linearRoughness = p.roughness * p.roughness;
    p.anisotropy = 0.0;

    vec3 clearCoatLobe = isEvaluateIBL(p, clearCoatNormal, shading_view, clearCoatNoV);
    Fr += clearCoatLobe * (specularAO * pixel.clearCoat);
#endif
}
#endif

//------------------------------------------------------------------------------
// IBL evaluation
//------------------------------------------------------------------------------

/**
 * Computes a specular occlusion term from the ambient occlusion term.
 */
float computeSpecularAO(float NoV, float ao, float roughness) {
#if defined(IBL_SPECULAR_OCCLUSION) && defined(MATERIAL_HAS_AMBIENT_OCCLUSION)
    return saturate(pow(NoV + ao, exp2(-16.0 * roughness - 1.0)) - 1.0 + ao);
#else
    return 1.0;
#endif
}

void evaluateClothIndirectDiffuseBRDF(const PixelParams pixel, inout float diffuse) {
#if defined(SHADING_MODEL_CLOTH)
#if defined(MATERIAL_HAS_SUBSURFACE_COLOR)
    // Simulate subsurface scattering with a wrap diffuse term
    diffuse *= Fd_Wrap(shading_NoV, 0.5);
#endif
#endif
}

void evaluateClearCoatIBL(const PixelParams pixel, float specularAO, inout vec3 Fd, inout vec3 Fr) {
#if defined(MATERIAL_HAS_CLEAR_COAT)
#if defined(MATERIAL_HAS_NORMAL) || defined(MATERIAL_HAS_CLEAR_COAT_NORMAL)
    // We want to use the geometric normal for the clear coat layer
    float clearCoatNoV = abs(dot(shading_clearCoatNormal, shading_view)) + FLT_EPS;
    vec3 clearCoatR = reflect(-shading_view, shading_clearCoatNormal);
#else
    float clearCoatNoV = shading_NoV;
    vec3 clearCoatR = shading_reflected;
#endif
    // The clear coat layer assumes an IOR of 1.5 (4% reflectance)
    float Fc = F_Schlick(0.04, 1.0, clearCoatNoV) * pixel.clearCoat;
    float attenuation = 1.0 - Fc;
    Fr *= sq(attenuation);
    Fr += specularIrradiance(clearCoatR, pixel.clearCoatRoughness) * (specularAO * Fc);
    Fd *= attenuation;
#endif
}

void evaluateSubsurfaceIBL(const PixelParams pixel, const vec3 diffuseIrradiance,
        inout vec3 Fd, inout vec3 Fr) {
#if defined(SHADING_MODEL_SUBSURFACE)
    vec3 viewIndependent = diffuseIrradiance;
    vec3 viewDependent = specularIrradiance(-shading_view, pixel.roughness, 1.0 + pixel.thickness);
    float attenuation = (1.0 - pixel.thickness) / (2.0 * PI);
    Fd += pixel.subsurfaceColor * (viewIndependent + viewDependent) * attenuation;
#elif defined(SHADING_MODEL_CLOTH) && defined(MATERIAL_HAS_SUBSURFACE_COLOR)
    Fd *= saturate(pixel.subsurfaceColor + shading_NoV);
#endif
}

void evaluateIBL(const MaterialInputs material, const PixelParams pixel, inout vec3 color) {
    // Apply transform here if we wanted to rotate the IBL
    vec3 n = shading_normal;
    vec3 r = getReflectedVector(pixel, n);

    float ao = material.ambientOcclusion;
    float specularAO = computeSpecularAO(shading_NoV, ao, pixel.roughness);

    // diffuse indirect
    float diffuseBRDF = ao; // Fd_Lambert() is baked in the SH below
    evaluateClothIndirectDiffuseBRDF(pixel, diffuseBRDF);

    vec3 diffuseIrradiance = diffuseIrradiance(n);
    vec3 Fd = pixel.diffuseColor * diffuseIrradiance * diffuseBRDF;

    // specular indirect
    vec3 Fr;
#if IBL_INTEGRATION == IBL_INTEGRATION_PREFILTERED_CUBEMAP
    Fr = specularDFG(pixel) * specularIrradiance(r, pixel.roughness);
    Fr *= specularAO * pixel.energyCompensation;
    evaluateClearCoatIBL(pixel, specularAO, Fd, Fr);
#elif IBL_INTEGRATION == IBL_INTEGRATION_IMPORTANCE_SAMPLING
    Fr = isEvaluateIBL(pixel, shading_normal, shading_view, shading_NoV);
    Fr *= specularAO * pixel.energyCompensation;
    isEvaluateClearCoatIBL(pixel, specularAO, Fd, Fr);
#endif
    evaluateSubsurfaceIBL(pixel, diffuseIrradiance, Fd, Fr);

    // Note: iblLuminance is already premultiplied by the exposure
    color.rgb += (Fd + Fr) * frameUniforms.iblLuminance;
}
