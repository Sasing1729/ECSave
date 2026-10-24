using ECSave;

namespace ECSave.Tests.Generator
{
    /// <summary>
    /// FrameBoundary モードの SaveScope。
    /// ConfigureSubScene で StreamingConsistencyMode.FrameBoundary を指定する。
    /// FrameBoundaryModeTests / FrameBoundaryExceptionTests から共有される。
    /// </summary>
    public sealed class GenFrameBoundaryScope : SaveScope
    {
        protected override void Configure(ISaveDefinitionBuilder b)
        {
            b.RegisterComponent<GenHealth>();
            b.WorldId("GenFrameBoundaryWorld");
        }

        protected override void ConfigureSubScene(ISubSceneDefinitionBuilder b)
        {
            b.WithStreamingConsistency(StreamingConsistencyMode.FrameBoundary);
        }
    }
}
