// Independent Windows SDK control: no PingXu, .NET or P/Invoke planning/marshalling.
// This executable only queries CCD and calls SetDisplayConfig with the constant 0x60.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <algorithm>
#include <cstring>
#include <iostream>
#include <stdexcept>
#include <vector>

struct State {
    std::vector<DISPLAYCONFIG_PATH_INFO> paths;
    std::vector<DISPLAYCONFIG_MODE_INFO> modes;
};

static State read() {
    for (int attempt = 0; attempt < 4; ++attempt) {
        UINT32 np = 0, nm = 0;
        LONG error = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, &np, &nm);
        if (error == ERROR_INSUFFICIENT_BUFFER) continue;
        if (error != ERROR_SUCCESS || np > 65536 || nm > 65536)
            throw std::runtime_error("GetDisplayConfigBufferSizes failed: " + std::to_string(error));
        State s{std::vector<DISPLAYCONFIG_PATH_INFO>(np), std::vector<DISPLAYCONFIG_MODE_INFO>(nm)};
        error = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, &np, s.paths.data(), &nm, s.modes.data(), nullptr);
        if (error == ERROR_INSUFFICIENT_BUFFER) continue;
        if (error != ERROR_SUCCESS) throw std::runtime_error("QueryDisplayConfig failed: " + std::to_string(error));
        s.paths.resize(np); s.modes.resize(nm);
        return s;
    }
    throw std::runtime_error("CCD topology kept changing");
}

static LONG validate(const char* label, State s) {
    constexpr UINT32 flags = SDC_VALIDATE | SDC_USE_SUPPLIED_DISPLAY_CONFIG;
    static_assert(flags == 0x60);
    const LONG error = SetDisplayConfig(static_cast<UINT32>(s.paths.size()), s.paths.data(),
        static_cast<UINT32>(s.modes.size()), s.modes.data(), flags);
    std::cout << label << ": Windows error " << error << " (flags=0x60)\n";
    return error;
}

template<typename T> static bool same(const std::vector<T>& a, const std::vector<T>& b) {
    return a.size() == b.size() && (a.empty() || std::memcmp(a.data(), b.data(), a.size() * sizeof(T)) == 0);
}

int main() {
    try {
        const State original = read();
        if (original.paths.size() != 1) throw std::runtime_error("This minimal control requires exactly one active output");
        const auto& path = original.paths[0];
        if (path.flags != DISPLAYCONFIG_PATH_ACTIVE || path.targetInfo.rotation != DISPLAYCONFIG_ROTATION_IDENTITY)
            throw std::runtime_error("This control requires a legacy landscape path");
        const auto si = path.sourceInfo.modeInfoIdx, ti = path.targetInfo.modeInfoIdx;
        if (si >= original.modes.size() || ti >= original.modes.size() ||
            original.modes[si].infoType != DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE ||
            original.modes[ti].infoType != DISPLAYCONFIG_MODE_INFO_TYPE_TARGET)
            throw std::runtime_error("Invalid mode references");
        std::cout << "SDK ABI: path=" << sizeof(DISPLAYCONFIG_PATH_INFO) << " mode=" << sizeof(DISPLAYCONFIG_MODE_INFO)
            << "; source=" << original.modes[si].sourceMode.width << 'x' << original.modes[si].sourceMode.height << '\n';
        const LONG baseline = validate("native/current", original);
        if (baseline != ERROR_SUCCESS) return 1;
        State rotated = original;
        rotated.paths[0].targetInfo.rotation = DISPLAYCONFIG_ROTATION_ROTATE90;
        validate("native/rotation-only", rotated);
        std::swap(rotated.modes[si].sourceMode.width, rotated.modes[si].sourceMode.height);
        const LONG portrait = validate("native/rotation-and-footprint", rotated);
        // Compact to a single SOURCE mode, letting Windows resolve the target timing.
        State solved = rotated;
        solved.modes = {rotated.modes[si]};
        solved.paths[0].sourceInfo.modeInfoIdx = 0;
        solved.paths[0].targetInfo.modeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
        validate("native/portrait-with-driver-target-mode", solved);
        const State after = read();
        if (!same(original.paths, after.paths) || !same(original.modes, after.modes))
            throw std::runtime_error("Native state changed; no restore attempted");
        std::cout << "Native readback unchanged (all active path/mode bytes).\n";
        return portrait == ERROR_SUCCESS ? 0 : 1;
    }
    catch (const std::exception& e) { std::cerr << e.what() << '\n'; return 1; }
}
