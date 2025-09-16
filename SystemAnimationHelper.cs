using System;
using System.Runtime.InteropServices;

namespace TrayChrome
{
    /// <summary>
    /// 系统动画设置检测帮助类
    /// </summary>
    public static class SystemAnimationHelper
    {
        // Win32 API 常量
        private const int SPI_GETCLIENTAREAANIMATION = 0x1042;
        private const int SPI_GETANIMATION = 0x0048;
        
        // Win32 API 声明
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfoW(
            uint uiAction,
            uint uiParam,
            ref bool pvParam,
            uint fWinIni);
        
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfoW(
            uint uiAction,
            uint uiParam,
            ref ANIMATIONINFO pvParam,
            uint fWinIni);
        
        [StructLayout(LayoutKind.Sequential)]
        private struct ANIMATIONINFO
        {
            public uint cbSize;
            public int iMinAnimate;
        }
        
        /// <summary>
        /// 检测系统是否启用了动画效果
        /// </summary>
        /// <returns>true表示启用动画，false表示禁用动画</returns>
        public static bool IsSystemAnimationEnabled()
        {
            try
            {
                // 方法1：检查客户区动画设置 (Windows Vista+)
                bool clientAreaAnimation = true;
                if (SystemParametersInfoW(SPI_GETCLIENTAREAANIMATION, 0, ref clientAreaAnimation, 0))
                {
                    if (!clientAreaAnimation)
                        return false;
                }
                
                // 方法2：检查窗口动画设置 (Windows 2000+)
                ANIMATIONINFO animInfo = new ANIMATIONINFO();
                animInfo.cbSize = (uint)Marshal.SizeOf(animInfo);
                
                if (SystemParametersInfoW(SPI_GETANIMATION, animInfo.cbSize, ref animInfo, 0))
                {
                    // iMinAnimate为0表示禁用动画
                    return animInfo.iMinAnimate != 0;
                }
                
                // 如果API调用失败，默认启用动画
                return true;
            }
            catch
            {
                // 出现异常时默认启用动画
                return true;
            }
        }
        
        /// <summary>
        /// 检测是否应该禁用动画
        /// 综合考虑系统设置和用户偏好
        /// </summary>
        /// <param name="userPreference">用户在应用中的动画偏好设置</param>
        /// <returns>true表示应该禁用动画</returns>
        public static bool ShouldDisableAnimation(bool userPreference)
        {
            // 如果用户明确禁用了动画，则禁用
            if (!userPreference)
                return true;
                
            // 如果系统禁用了动画，则禁用
            if (!IsSystemAnimationEnabled())
                return true;
                
            // 否则启用动画
            return false;
        }
    }
}