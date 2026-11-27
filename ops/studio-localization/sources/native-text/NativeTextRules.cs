using System;
using System.Collections;
using System.Collections.Generic;

namespace Codex.StudioNativePluginText
{
    internal sealed class Correction
    {
        internal readonly string Key;
        internal readonly string Before;
        internal readonly string Value;

        internal Correction(string key, string before, string value)
        {
            Key = key;
            Before = before;
            Value = value;
        }
    }

    internal static class NativeTextRules
    {
        internal static readonly Correction[] Corrections =
        {
            new Correction("Framerate", "帧率", "采集帧率"),
            new Correction("LimitBy", "限制", "录制限制方式"),
            new Correction("LimitByFrames", "帧率", "帧数"),
            new Correction("LimitBySeconds", "时间", "秒数"),
            new Correction("LimitByPrewarmLoopCount", "预热循环计数", "预热循环次数"),
            new Correction("LimitByLoopsToRecord", "循环记录", "录制循环次数"),
            new Correction("LimitByLimitCount", "循环次数", "限制数量"),
            new Correction("EmptyScene", "比如", "编码前清空场景"),
            new Correction("ParallelScreenshotEncoding", "并行编码（测试版", "并行编码（测试版）"),
            new Correction("PrewarmingAnimation", "动画速率", "正在预热动画"),
            new Correction("GeneratingVideo", "生成进度", "正在生成视频"),
            new Correction("CaptureModeImmediate", "高速", "即时"),
            new Correction("RotationNone", "默认", "无旋转"),
            new Correction("Rotation90", "旋转90°", "顺时针 90°"),
            new Correction("Rotation270", "旋转-90°", "逆时针 90°"),
            new Correction("MP4Quality", "质量（数值越低越好，推荐使用 18）", "质量"),
            new Correction("CaptureSettingsHeading", "捕捉设置", "采集设置"),
            new Correction("ExtensionTooltip", "[MP4] 通用且广泛支持的视频格式\\n[WEBM] 针对网页优化的视频格式\\n[GIF] 旧的短动画图像格式\\n[MOV] 专业剪辑使用的视频格式（占用空间较大）\\n[WEBP] 针对网页优化的图像格式（支持动画）\\n[AVIF] 基于AV1编码的现代图像格式（支持动画）", "[MP4] 通用且广泛支持的视频格式\\n[WEBM] 针对网页优化的视频格式\\n[GIF] 用于短动画和循环的旧图像格式\\n[MOV] 专业剪辑使用的视频格式（占用空间较大）\\n[WEBP] 针对网页优化的图像格式（支持动画）\\n[AVIF] 基于 AV1 编码的现代图像格式（支持动画）\\n[MKV]（FFV1）适合归档和长期保存。"),
            new Correction("MP4CodecTooltip", "[H.264] 广泛使用和支持。推荐使用。\\n[H.265] 更高效，但兼容性略差。", "[H.264] 使用广泛、兼容性好，推荐。\\n[H.265] 压缩效率更高，但部分环境可能不支持。\\n[AV1] 压缩效率最高，但部分环境可能不支持。"),
            new Correction("MOVCodecTooltip", "[ProRes4444] 生成用于专业视频编辑的超大文件。", "[ProRes] 生成面向专业视频剪辑的大体积文件。"),
            new Correction("AVIFCodecTooltip", "[Libaom-av1] 广泛支持，无损画质，编码较慢。\\n[Libsvt-av1] 良好画质（有损），编码快速。", "[Libaom-av1] 支持广泛，无损质量，编码较慢。\\n[Libsvt-av1] 画质良好，编码较快。"),
            new Correction("FlacCholeskyPasses", "LPC 通道", "LPC 迭代次数"),
            new Correction("FlacPredictionOrder", "预测顺序", "预测阶数"),
            new Correction("OpusFramesize", "帧尺寸", "帧时长"),
            new Correction("AudioPluginVolumeTooltip", "警告：音量过大可能会导致音频爆音！", "警告：音量过大可能导致音频削波！"),
        };

        internal static int Apply(IDictionary strings, Type keyType)
        {
            var changes = new List<KeyValuePair<object, string>>();
            foreach (var correction in Corrections)
            {
                object key = Enum.Parse(keyType, correction.Key);
                if (!strings.Contains(key))
                    throw new InvalidOperationException("Missing native text key " + correction.Key);
                string current = strings[key] as string;
                if (current == correction.Value) continue;
                if (current != correction.Before)
                    throw new InvalidOperationException("Native text differs for " + correction.Key);
                changes.Add(new KeyValuePair<object, string>(key, correction.Value));
            }
            foreach (var change in changes) strings[change.Key] = change.Value;
            return changes.Count;
        }
    }
}
