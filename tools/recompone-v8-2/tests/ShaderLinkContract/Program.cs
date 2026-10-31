using RecompOne.Runtime.Enhanced;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Glfw;

// Links the renderer's programs on the real driver. NVIDIA rejects at link
// time what other vendors accept (C7001 when varyings exceed its 31 slots),
// so only a driver-backed check proves the shipping shaders build here.
GlfwWindowing.Use();
using var window = Window.Create(WindowOptions.Default with
{
    Title = "ShaderLinkContract", Size = new Vector2D<int>(64, 64), IsVisible = false,
    API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.Default, new APIVersion(4, 5))
});
window.Initialize();
var gl = GL.GetApi(window);
Console.WriteLine($"renderer={gl.GetStringS(StringName.Vendor)} | {gl.GetStringS(StringName.Renderer)} | {gl.GetStringS(StringName.Version)}");
gl.GetInteger(GetPName.MaxVaryingVectors, out int maxVaryings);
gl.GetInteger(GetPName.MaxVertexAttribs, out int maxAttributes);
Console.WriteLine($"max-varying-vectors={maxVaryings} max-vertex-attribs={maxAttributes}");

int failures = 0;
foreach (var (name, vs, fs) in new[]
{
    ("prim", GlShaders.PrimVs, GlShaders.PrimFs),
    ("present", GlShaders.FullscreenVs, GlShaders.PresentFs),
    ("present24", GlShaders.FullscreenVs, GlShaders.Present24Fs),
})
{
    uint program = GlShaders.Build(gl, vs, fs, name);
    if (program == 0) { failures++; Console.WriteLine($"FAIL {name}"); continue; }
    gl.GetProgram(program, ProgramPropertyARB.ActiveAttributes, out int attributes);
    Console.WriteLine($"PASS {name} attributes={attributes}");
    gl.DeleteProgram(program);
}
window.Close();
return failures == 0 ? 0 : 1;
