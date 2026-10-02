namespace AoE4OverlayCS.Services
{
    /// <summary>
    /// Overlay 缩放边界。窗口尺寸 = 内容自然尺寸 × 用户倍率，倍率限制在 [MinScale, MaxScale]。
    /// 缩放不再从窗口尺寸反推（会导致多次搜索后逐次变小），倍率仅由用户拖拽或历史几何决定。
    /// </summary>
    public static class OverlayScaleCalculator
    {
        public const double MinScale = 0.5;
        public const double MaxScale = 3.0;
    }
}