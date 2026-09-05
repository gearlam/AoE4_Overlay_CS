using System;

namespace AoE4OverlayCS.Services
{
    /// <summary>
    /// 计算 Overlay 内容随窗口尺寸变化的等比缩放比例。
    /// </summary>
    public static class OverlayScaleCalculator
    {
        public const double MinScale = 0.5;
        public const double MaxScale = 3.0;

        /// <summary>
        /// 以 min(宽比, 高比) 计算等比缩放比例，并 clamp 到 [MinScale, MaxScale]。
        /// 同时限制内容不溢出窗口：左右/上下各保留 1px 空隙，避免放大后内容贴边被裁剪。
        /// 任一尺寸无效（<=0）时返回 1.0，避免除零与异常布局。
        /// </summary>
        public static double ComputeScale(double clientWidth, double clientHeight, double baseWidth, double baseHeight)
        {
            if (clientWidth <= 0 || clientHeight <= 0 || baseWidth <= 0 || baseHeight <= 0)
                return 1.0;

            double scaleX = clientWidth / baseWidth;
            double scaleY = clientHeight / baseHeight;
            double scale = Math.Min(scaleX, scaleY);

            // 内容不溢出窗口：左右/上下各保留 1px 空隙
            double maxScaleByWidth = (clientWidth - 2) / baseWidth;
            double maxScaleByHeight = (clientHeight - 2) / baseHeight;
            scale = Math.Min(scale, Math.Min(maxScaleByWidth, maxScaleByHeight));

            return Math.Clamp(scale, MinScale, MaxScale);
        }
    }
}
