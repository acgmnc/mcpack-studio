using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace McPackStudio
{
    /// <summary>
    /// 无头自测：构造真实的旧版资源包，跑完整转换流程并逐项断言。
    /// 用 --selftest [输出文件] 运行。
    /// </summary>
    internal static class SelfTest
    {
        private static readonly StringBuilder Out = new StringBuilder();
        private static int _pass, _fail;
        private static readonly List<string> _failures = new List<string>();
        private static string _root;

        public static int Run(string outFile)
        {
            _root = Path.Combine(Path.GetTempPath(), "mcpack_selftest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);

            try
            {
                Section("1. 版本表");
                TestVersionTable();

                Section("2. JSON 读写");
                TestJson();

                Section("3. 语言键迁移规则");
                TestLangKeys();

                Section("4. 旧版资源包 → 1.20.1（完整迁移）");
                TestLegacyToModern();

                Section("5. 只改元数据模式");
                TestMetadataOnly();

                Section("6. 新版 → 旧版（反向转换）");
                TestModernToLegacy();

                Section("7. 1.21.9+ 的 min_format / max_format");
                TestNewFormat();

                Section("8. ZIP 输入 / ZIP 输出");
                TestZipRoundTrip();

                Section("9. 重命名冲突处理");
                TestConflict();

                Section("10. 边界与错误处理");
                TestEdgeCases();

                Section("11. 合并资源包");
                TestMerge();

                Section("12. PNG 编码器");
                TestPngWriter();

                Section("13. 新建资源包骨架");
                TestScaffold();

                Section("14. 批量重命名");
                TestRename();

                Section("15. NBT 数据模型与 SNBT");
                TestNbtModel();

                Section("16. NBT 二进制读写");
                TestNbtIo();
            }
            catch (Exception e)
            {
                _fail++;
                _failures.Add("自测异常中断：" + e.Message + "\n" + e.StackTrace);
                Line("!! 自测异常中断：" + e.Message);
                Line(e.StackTrace);
            }
            finally
            {
                try { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
                catch { }
            }

            Line("");
            Line(new string('=', 56));
            Line("通过 " + _pass + " 项，失败 " + _fail + " 项");
            if (_failures.Count > 0)
            {
                Line("");
                Line("失败明细：");
                foreach (string f in _failures) Line("  - " + f);
            }
            Line(new string('=', 56));

            if (!string.IsNullOrEmpty(outFile))
            {
                try { File.WriteAllText(outFile, Out.ToString(), new UTF8Encoding(false)); }
                catch { }
            }
            return _fail == 0 ? 0 : 1;
        }

        /* ============================================================ */

        private static void Line(string s) { Out.AppendLine(s); }

        private static void Section(string title)
        {
            Line("");
            Line("=== " + title + " ===");
        }

        private static void Check(string name, bool ok, string detail = null)
        {
            if (ok) { _pass++; Line("  ✓ " + name); }
            else
            {
                _fail++;
                string msg = name + (detail != null ? "  —— " + detail : "");
                _failures.Add(msg);
                Line("  ✗ " + msg);
            }
        }

        private static void Eq(string name, object actual, object expected)
        {
            bool ok = Equals(actual, expected);
            Check(name, ok, ok ? null : "期望 [" + (expected ?? "null") + "]，实际 [" + (actual ?? "null") + "]");
        }

        /* ============================================================ */
        /* 1. 版本表                                                     */
        /* ============================================================ */

        private static void TestVersionTable()
        {
            var all = Settings.AllVersions();
            Check("版本表已加载", all.Count > 30, "实际 " + all.Count + " 条");
            Check("版本表按 format 升序", all.Zip(all.Skip(1), (a, b) => a.Format <= b.Format).All(x => x));

            Eq("pack_format 46 只取正式版", Settings.Describe(Settings.FindByFormat(46)), "1.21.4");
            Eq("pack_format 34 → 1.21 – 1.21.1", Settings.Describe(Settings.FindByFormat(34)), "1.21 – 1.21.1");
            Eq("pack_format 15 → 1.20 – 1.20.1", Settings.Describe(Settings.FindByFormat(15)), "1.20 – 1.20.1");

            Eq("按版本号定位 1.12.2 → 3", Settings.FindFormatByVersion("1.12.2"), 3);
            Eq("按版本号定位 1.20.1 → 15", Settings.FindFormatByVersion("1.20.1"), 15);
            Eq("按版本号定位 1.21.4 → 46", Settings.FindFormatByVersion("1.21.4"), 46);
            Eq("未知版本返回 null", Settings.FindFormatByVersion("9.9.9"), null);

            Check("正式版判断：1.20.1 是", Settings.IsRelease("1.20.1"));
            Check("正式版判断：1.20.1-rc1 不是", !Settings.IsRelease("1.20.1-rc1"));
            Check("正式版判断：23w31a 不是", !Settings.IsRelease("23w31a"));
            Check("正式版判断：1.20 是", Settings.IsRelease("1.20"));

            Eq("阶段判断 format 3", Settings.DescribeStage(3).StartsWith("Legacy"), true);
            Eq("阶段判断 format 46", Settings.DescribeStage(46).StartsWith("Items Folder"), true);
            Eq("阶段判断 format 75", Settings.DescribeStage(75).StartsWith("New Format"), true);
        }

        /* ============================================================ */
        /* 2. JSON                                                      */
        /* ============================================================ */

        private static void TestJson()
        {
            JsonValue v; string err;

            Check("解析简单对象", Json.TryParse("{\"a\":1}", out v, out err));
            Eq("取整数值", v.Get("a").AsInt(), 1);

            Check("解析带 BOM 与注释前空白", Json.TryParse("\uFEFF  {\n \"pack\": {\"pack_format\": 34}\n}", out v, out err));
            Eq("嵌套取值", v.Get("pack").Get("pack_format").AsInt(), 34);

            Check("解析转义", Json.TryParse("{\"k\":\"a\\nb\\u4e2d\"}", out v, out err));
            Eq("转义还原", v.Get("k").StringValue, "a\nb中");

            Check("解析数组", Json.TryParse("{\"a\":[1,2,3]}", out v, out err));
            Eq("数组长度", v.Get("a").Items.Count, 3);

            Check("拒绝多余内容", !Json.TryParse("{} garbage", out v, out err));
            Check("拒绝未闭合", !Json.TryParse("{\"a\":1", out v, out err));
            Check("拒绝非法转义", !Json.TryParse("{\"a\":\"\\x\"}", out v, out err));

            // 保序
            Check("解析保序", Json.TryParse("{\"z\":1,\"a\":2,\"m\":3}", out v, out err));
            Eq("键顺序保持", string.Join(",", v.Keys().ToArray()), "z,a,m");

            var obj = JsonValue.Obj();
            obj.Set("b", JsonValue.Num(1));
            obj.Set("a", JsonValue.Num(2));
            obj.Set("b", JsonValue.Num(9));   // 覆盖不应改变位置
            Eq("覆盖不改顺序", string.Join(",", obj.Keys().ToArray()), "b,a");
            Eq("覆盖生效", obj.Get("b").AsInt(), 9);

            string pretty = Json.Write(obj, true);
            Check("输出可被重新解析", Json.TryParse(pretty, out v, out err));
            Eq("往返后值一致", v.Get("b").AsInt(), 9);

            Eq("整数不输出小数点", Json.Write(JsonValue.Num(34), false), "34");
            Eq("小数正常输出", Json.Write(JsonValue.Num(1.5), false), "1.5");
            Eq("字符串转义输出", Json.Write(JsonValue.Str("a\"b"), false), "\"a\\\"b\"");

            // 对象形式的 pack_format
            Check("解析范围写法 pack_format",
                Json.TryParse("{\"pack\":{\"pack_format\":{\"min_inclusive\":46,\"max_inclusive\":99}}}", out v, out err));
            var pv = new List<string>();
            var disc = _root;
            var info = PackAnalyzer.ReadFormat(WriteTempMeta("{\"pack\":{\"pack_format\":{\"min_inclusive\":46,\"max_inclusive\":99}}}"), null);
            Eq("对象写法取 min_inclusive", info.PackFormat, 46);

            Check("识别为新格式", info.UsesNewFormat);
        }

        private static string WriteTempMeta(string json)
        {
            string p = Path.Combine(_root, "probe_" + Guid.NewGuid().ToString("N") + ".mcmeta");
            File.WriteAllText(p, json, new UTF8Encoding(false));
            return p;
        }

        /* ============================================================ */
        /* 3. 语言键                                                     */
        /* ============================================================ */

        private static void TestLangKeys()
        {
            Eq("block.stone → block.minecraft.stone",
                MigrationRules.MigrateLangKey("block.stone"), "block.minecraft.stone");
            Eq("item.apple → item.minecraft.apple",
                MigrationRules.MigrateLangKey("item.apple"), "item.minecraft.apple");
            Eq("tile.stone → block.minecraft.stone（旧版 tile 前缀）",
                MigrationRules.MigrateLangKey("tile.stone"), "block.minecraft.stone");
            Eq("entity.creeper → entity.minecraft.creeper",
                MigrationRules.MigrateLangKey("entity.creeper"), "entity.minecraft.creeper");

            Eq("已带命名空间的不重复加",
                MigrationRules.MigrateLangKey("block.minecraft.stone"), "block.minecraft.stone");
            Eq("item 已带命名空间也不动",
                MigrationRules.MigrateLangKey("item.minecraft.apple"), "item.minecraft.apple");

            Eq("mod 自定义键保持原样",
                MigrationRules.MigrateLangKey("block.mymod.custom"), "block.mymod.custom");
            Eq("普通键不动", MigrationRules.MigrateLangKey("gui.done"), "gui.done");
            Eq("三段的 tile 旧键也视为 mod 键不动",
                MigrationRules.MigrateLangKey("tile.foo.bar"), "tile.foo.bar");
            Eq("空字符串安全", MigrationRules.MigrateLangKey(""), "");

            Eq("地区码 en_US → en_us", MigrationRules.NormalizeLocale("en_US"), "en_us");
            Eq("地区码 zh_CN → zh_cn", MigrationRules.NormalizeLocale("zh_CN"), "zh_cn");
            Eq("地区码 en_us 不变", MigrationRules.NormalizeLocale("en_us"), "en_us");
        }

        /* ============================================================ */
        /* 4. 旧版 → 新版                                               */
        /* ============================================================ */

        private static void TestLegacyToModern()
        {
            string src = Path.Combine(_root, "legacy_pack");
            BuildLegacyPack(src);
            string outDir = Path.Combine(_root, "legacy_out");

            var analysis = PackAnalyzer.Analyze(src);
            Check("分析旧版包成功", analysis.Ok, analysis.Error);
            Eq("识别 pack_format = 3", analysis.Format.PackFormat, 3);
            Check("描述按旧版解析", analysis.Format.Description != null && analysis.Format.Description.Contains("Legacy"));

            var opt = new ConvertOptions
            {
                Mode = OutputMode.Folder,
                OutputPath = outDir,
                TargetFormat = 15,                 // 1.20 – 1.20.1
                TargetVersionLabel = "1.20 – 1.20.1",
                MigrateAssets = true,
                MigrateLangContent = true,
            };

            var plan = PlanBuilder.Build(analysis, opt);
            Line("  计划：" + plan.Describe());
            Line("  规则：" + string.Join(", ", plan.RuleIds.ToArray()));

            Check("触发了纹理目录迁移", plan.RuleIds.Contains("flatten-textures-blocks"));
            Check("触发了语言文件规范化", plan.RuleIds.Contains("flatten-lang-file"));
            Check("触发了语言键迁移", plan.RuleIds.Contains("flatten-lang-keys"));
            Check("有计划中的重命名", plan.RenameCount > 0, "重命名 " + plan.RenameCount);
            Check("有计划中的改写", plan.RewriteCount > 0, "改写 " + plan.RewriteCount);

            var report = ConvertExecutor.Execute(plan, null);
            Check("转换执行成功", report.Success, report.Error);
            if (!report.Success) return;

            /* --- 目录结构断言 --- */
            Check("新目录 textures/block 存在", Directory.Exists(Path.Combine(outDir, "assets/minecraft/textures/block")));
            Check("新目录 textures/item 存在", Directory.Exists(Path.Combine(outDir, "assets/minecraft/textures/item")));
            Check("旧目录 textures/blocks 已消失", !Directory.Exists(Path.Combine(outDir, "assets/minecraft/textures/blocks")));
            Check("旧目录 textures/items 已消失", !Directory.Exists(Path.Combine(outDir, "assets/minecraft/textures/items")));

            Check("方块纹理已改名到位", File.Exists(Path.Combine(outDir, "assets/minecraft/textures/block/stone.png")));
            Check("物品纹理已改名到位", File.Exists(Path.Combine(outDir, "assets/minecraft/textures/item/apple.png")));
            Check("自定义命名空间也迁移", File.Exists(Path.Combine(outDir, "assets/mymod/textures/block/ore.png")));
            Check("非迁移目录原样保留", File.Exists(Path.Combine(outDir, "assets/minecraft/textures/gui/widgets.png")));
            Check("模型文件原样保留", File.Exists(Path.Combine(outDir, "assets/minecraft/models/block/stone.json")));

            /* --- 语言文件 --- */
            Check("语言文件已改成 .json", File.Exists(Path.Combine(outDir, "assets/minecraft/lang/en_us.json")));
            Check("旧 .lang 文件不再存在", !File.Exists(Path.Combine(outDir, "assets/minecraft/lang/en_US.lang")));

            string langPath = Path.Combine(outDir, "assets/minecraft/lang/en_us.json");
            if (File.Exists(langPath))
            {
                string langRaw = File.ReadAllText(langPath);
                Line("  [调试] 语言文件前 160 字符：" + langRaw.Substring(0, Math.Min(160, langRaw.Length)).Replace("\n", "\\n"));
                JsonValue lang; string lerr;
                Check("语言文件是合法 JSON", Json.TryParse(langRaw, out lang, out lerr), lerr);
                if (lang != null && lang.IsObject)
                {
                    Check("block.stone 迁移为 block.minecraft.stone",
                        lang.Has("block.minecraft.stone"), "实际键：" + string.Join(",", lang.Keys().Take(8).ToArray()));
                    Check("item.apple 迁移为 item.minecraft.apple", lang.Has("item.minecraft.apple"));
                    Check("tile.grass 迁移为 block.minecraft.grass", lang.Has("block.minecraft.grass"));
                    Check("gui.done 保持不动", lang.Has("gui.done"));
                    Check("语言值没有被改动",
                        lang.Get("block.minecraft.stone") != null && lang.Get("block.minecraft.stone").StringValue == "Stone");
                }
            }

            /* --- pack.mcmeta --- */
            string metaPath = Path.Combine(outDir, "pack.mcmeta");
            JsonValue meta = null;
            string merr = null;
            bool metaOk = File.Exists(metaPath) && Json.TryParse(File.ReadAllText(metaPath), out meta, out merr);
            Check("pack.mcmeta 存在且合法", metaOk, merr);
            if (meta != null)
            {
                var pack = meta.Get("pack");
                Eq("pack_format 已写成 15", pack.Get("pack_format").AsInt(), 15);
                Check("描述保留了原文", pack.Get("description").StringValue.Contains("Legacy"));
                Check("描述追加了转换标记", pack.Get("description").StringValue.Contains("MCPack Studio"));
                Check("旧格式不写 min/max_format", !pack.Has("min_format") && !pack.Has("max_format"));
            }

            /* --- 输出能被自己重新分析 --- */
            var re = PackAnalyzer.Analyze(outDir);
            Check("输出可被重新分析", re.Ok, re.Error);
            Eq("重新分析得到 pack_format 15", re.Format.PackFormat, 15);
            Check("重新分析无纹理目录告警", !re.Warnings.Any(w => w.Contains("pack.mcmeta")));
        }

        /* ============================================================ */
        /* 5. 只改元数据                                                 */
        /* ============================================================ */

        private static void TestMetadataOnly()
        {
            string src = Path.Combine(_root, "meta_only_src");
            BuildLegacyPack(src);
            string outDir = Path.Combine(_root, "meta_only_out");

            var analysis = PackAnalyzer.Analyze(src);
            var opt = new ConvertOptions
            {
                Mode = OutputMode.Folder,
                OutputPath = outDir,
                TargetFormat = 15,
                TargetVersionLabel = "1.20 – 1.20.1",
                MigrateAssets = false,
            };
            var plan = PlanBuilder.Build(analysis, opt);
            Check("只改元数据时不触发迁移规则", plan.RuleIds.Count == 0, string.Join(",", plan.RuleIds.ToArray()));
            Eq("只改元数据时没有重命名", plan.RenameCount, 0);

            var report = ConvertExecutor.Execute(plan, null);
            Check("执行成功", report.Success, report.Error);
            if (!report.Success) return;

            Check("旧目录 textures/blocks 仍然存在", Directory.Exists(Path.Combine(outDir, "assets/minecraft/textures/blocks")));
            Check("旧 .lang 文件仍然存在", File.Exists(Path.Combine(outDir, "assets/minecraft/lang/en_US.lang")));

            JsonValue meta;
            string err;
            Json.TryParse(File.ReadAllText(Path.Combine(outDir, "pack.mcmeta")), out meta, out err);
            Eq("pack_format 已更新", meta.Get("pack").Get("pack_format").AsInt(), 15);
        }

        /* ============================================================ */
        /* 6. 反向转换                                                   */
        /* ============================================================ */

        private static void TestModernToLegacy()
        {
            string src = Path.Combine(_root, "modern_src");
            Directory.CreateDirectory(Path.Combine(src, "assets/minecraft/textures/block"));
            Directory.CreateDirectory(Path.Combine(src, "assets/minecraft/textures/item"));
            Directory.CreateDirectory(Path.Combine(src, "assets/minecraft/lang"));
            File.WriteAllText(Path.Combine(src, "pack.mcmeta"),
                "{\n  \"pack\": {\n    \"pack_format\": 15,\n    \"description\": \"Modern\"\n  }\n}", new UTF8Encoding(false));
            File.WriteAllBytes(Path.Combine(src, "assets/minecraft/textures/block/stone.png"), new byte[] { 1, 2, 3 });
            File.WriteAllBytes(Path.Combine(src, "assets/minecraft/textures/item/apple.png"), new byte[] { 4, 5, 6 });
            File.WriteAllText(Path.Combine(src, "assets/minecraft/lang/en_us.json"),
                "{\n  \"block.minecraft.stone\": \"Stone\",\n  \"gui.done\": \"Done\"\n}", new UTF8Encoding(false));

            string outDir = Path.Combine(_root, "modern_out");
            var analysis = PackAnalyzer.Analyze(src);
            var opt = new ConvertOptions
            {
                Mode = OutputMode.Folder,
                OutputPath = outDir,
                TargetFormat = 3,
                TargetVersionLabel = "1.11 – 1.12.2",
                MigrateAssets = true,
                MigrateLangContent = true,
            };
            var plan = PlanBuilder.Build(analysis, opt);
            Line("  反向计划：" + plan.Describe());

            var report = ConvertExecutor.Execute(plan, null);
            Check("反向转换成功", report.Success, report.Error);
            if (!report.Success) return;

            Line("  [调试] 反向输出文件列表：");
            foreach (string f in Directory.GetFiles(outDir, "*", SearchOption.AllDirectories).OrderBy(x => x))
            {
                Line("     " + ZipIo.RelPath(outDir, f));
            }

            Check("纹理回到 textures/blocks", File.Exists(Path.Combine(outDir, "assets/minecraft/textures/blocks/stone.png")));
            Check("物品回到 textures/items", File.Exists(Path.Combine(outDir, "assets/minecraft/textures/items/apple.png")));
            Check("语言文件回到大写 .lang", File.Exists(Path.Combine(outDir, "assets/minecraft/lang/en_US.lang")));

            JsonValue meta; string err;
            Json.TryParse(File.ReadAllText(Path.Combine(outDir, "pack.mcmeta")), out meta, out err);
            Eq("pack_format 降回 3", meta.Get("pack").Get("pack_format").AsInt(), 3);
        }

        /* ============================================================ */
        /* 7. 新格式 min/max_format                                      */
        /* ============================================================ */

        private static void TestNewFormat()
        {
            string src = Path.Combine(_root, "newfmt_src");
            Directory.CreateDirectory(Path.Combine(src, "assets/minecraft/textures/block"));
            File.WriteAllText(Path.Combine(src, "pack.mcmeta"),
                "{\n  \"pack\": {\n    \"pack_format\": 69,\n    \"min_format\": [69, 0],\n    \"max_format\": [999, 0],\n    \"description\": \"NewFmt\"\n  }\n}",
                new UTF8Encoding(false));

            var analysis = PackAnalyzer.Analyze(src);
            Check("识别为新格式", analysis.Format.UsesNewFormat);
            Eq("读取 pack_format", analysis.Format.PackFormat, 69);

            string outDir = Path.Combine(_root, "newfmt_out");
            var opt = new ConvertOptions
            {
                Mode = OutputMode.Folder,
                OutputPath = outDir,
                TargetFormat = 75,
                TargetVersionLabel = "1.21.11",
                MigrateAssets = false,
            };
            var plan = PlanBuilder.Build(analysis, opt);
            var report = ConvertExecutor.Execute(plan, null);
            Check("转换成功", report.Success, report.Error);
            if (!report.Success) return;

            JsonValue meta; string err;
            Json.TryParse(File.ReadAllText(Path.Combine(outDir, "pack.mcmeta")), out meta, out err);
            var pack = meta.Get("pack");
            Eq("pack_format 更新到 75", pack.Get("pack_format").AsInt(), 75);
            Check("保留了 min_format", pack.Has("min_format"));
            Check("保留了 max_format", pack.Has("max_format"));
            Eq("min_format 首元素为新格式", pack.Get("min_format").Items[0].AsInt(), 75);
            Eq("max_format 首元素为 999", pack.Get("max_format").Items[0].AsInt(), 999);

            /* 从旧格式转到一个也是新格式的目标：不应凭空写出 min/max_format */
            string src2 = Path.Combine(_root, "newfmt_src2");
            Directory.CreateDirectory(src2);
            File.WriteAllText(Path.Combine(src2, "pack.mcmeta"),
                "{\"pack\":{\"pack_format\":15,\"description\":\"Old\"}}", new UTF8Encoding(false));
            string outDir2 = Path.Combine(_root, "newfmt_out2");
            var a2 = PackAnalyzer.Analyze(src2);
            var p2 = PlanBuilder.Build(a2, new ConvertOptions
            {
                Mode = OutputMode.Folder,
                OutputPath = outDir2,
                TargetFormat = 75,
                TargetVersionLabel = "1.21.11",
                MigrateAssets = false,
            });
            var r2 = ConvertExecutor.Execute(p2, null);
            Check("旧→新格式目标转换成功", r2.Success, r2.Error);
            JsonValue m2; string e2;
            Json.TryParse(File.ReadAllText(Path.Combine(outDir2, "pack.mcmeta")), out m2, out e2);
            Check("原文不是新格式则不写 min/max_format", !m2.Get("pack").Has("min_format"));
            Eq("pack_format 正确", m2.Get("pack").Get("pack_format").AsInt(), 75);

            /* 清理 supported_formats */
            string src3 = Path.Combine(_root, "newfmt_src3");
            Directory.CreateDirectory(src3);
            File.WriteAllText(Path.Combine(src3, "pack.mcmeta"),
                "{\"pack\":{\"pack_format\":34,\"supported_formats\":[34,99],\"description\":\"Sup\"}}", new UTF8Encoding(false));
            string outDir3 = Path.Combine(_root, "newfmt_out3");
            var a3 = PackAnalyzer.Analyze(src3);
            var p3 = PlanBuilder.Build(a3, new ConvertOptions
            {
                Mode = OutputMode.Folder,
                OutputPath = outDir3,
                TargetFormat = 46,
                TargetVersionLabel = "1.21.4",
                MigrateAssets = false,
            });
            ConvertExecutor.Execute(p3, null);
            JsonValue m3; string e3;
            Json.TryParse(File.ReadAllText(Path.Combine(outDir3, "pack.mcmeta")), out m3, out e3);
            Check("supported_formats 被清除", !m3.Get("pack").Has("supported_formats"));
            Eq("pack_format 更新", m3.Get("pack").Get("pack_format").AsInt(), 46);
        }

        /* ============================================================ */
        /* 8. ZIP                                                        */
        /* ============================================================ */

        private static void TestZipRoundTrip()
        {
            string src = Path.Combine(_root, "zip_src");
            BuildLegacyPack(src);
            string srcZip = Path.Combine(_root, "zip_src.zip");
            ZipIo.CreateFromDirectory(src, srcZip, null);
            Check("测试用 ZIP 已生成", File.Exists(srcZip));

            var analysis = PackAnalyzer.Analyze(srcZip);
            Check("能从 ZIP 分析", analysis.Ok, analysis.Error);
            Eq("ZIP 内 pack_format 读取正确", analysis.Format.PackFormat, 3);

            string outZip = Path.Combine(_root, "zip_out.zip");
            var opt = new ConvertOptions
            {
                Mode = OutputMode.Zip,
                OutputPath = outZip,
                TargetFormat = 15,
                TargetVersionLabel = "1.20 – 1.20.1",
                MigrateAssets = true,
                MigrateLangContent = true,
            };
            var plan = PlanBuilder.Build(analysis, opt);
            var report = ConvertExecutor.Execute(plan, null);
            Check("ZIP 输出转换成功", report.Success, report.Error);
            if (!report.Success) return;

            Check("输出 ZIP 存在", File.Exists(outZip));

            // 用独立方式检查 ZIP 内容
            using (var fs = File.OpenRead(outZip))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                var names = zip.Entries.Select(e => ZipIo.Normalize(e.FullName)).ToList();
                Check("ZIP 内有 textures/block/stone.png", names.Contains("assets/minecraft/textures/block/stone.png"));
                Check("ZIP 内没有 textures/blocks", !names.Any(n => n.StartsWith("assets/minecraft/textures/blocks/")));
                Check("ZIP 内有 lang/en_us.json", names.Contains("assets/minecraft/lang/en_us.json"));
                Check("ZIP 内有 pack.mcmeta", names.Contains("pack.mcmeta"));

                var metaEntry = zip.GetEntry("pack.mcmeta");
                string text;
                using (var sr = new StreamReader(metaEntry.Open(), Encoding.UTF8))
                {
                    text = sr.ReadToEnd();
                }
                JsonValue m; string err;
                Json.TryParse(text, out m, out err);
                Eq("ZIP 内 pack_format 已更新", m.Get("pack").Get("pack_format").AsInt(), 15);

                // 内容完整性：64 字节的假纹理应原样保留
                var pngEntry = zip.GetEntry("assets/minecraft/textures/block/texture_bytes.png");
                Check("ZIP 内存在用于校验字节的纹理", pngEntry != null);
                if (pngEntry != null)
                {
                    byte[] buf = new byte[pngEntry.Length];
                    using (var s = pngEntry.Open())
                    {
                        int read = 0;
                        while (read < buf.Length)
                        {
                            int n = s.Read(buf, read, buf.Length - read);
                            if (n <= 0) break;
                            read += n;
                        }
                    }
                    Eq("纹理字节数保持", buf.Length, 64);
                    bool same = true;
                    for (int i = 0; i < buf.Length; i++)
                    {
                        if (buf[i] != (byte)(i * 3 & 0xFF)) { same = false; break; }
                    }
                    Check("纹理字节内容逐字节一致", same);
                }
            }

            /* 带外层目录的 ZIP */
            string nestedZip = Path.Combine(_root, "nested.zip");
            using (var fs = File.Create(nestedZip))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                WriteEntry(zip, "MyPack/pack.mcmeta", "{\"pack\":{\"pack_format\":3,\"description\":\"Nested\"}}");
                WriteEntry(zip, "MyPack/assets/minecraft/textures/blocks/dirt.png", "xx");
            }
            string np, nerr;
            bool ok = ZipIo.TryFindPackRoot(nestedZip, out np, out nerr);
            Check("识别嵌套 ZIP 根前缀", ok && np == "MyPack/", "前缀=" + np);

            var na = PackAnalyzer.Analyze(nestedZip);
            Check("嵌套 ZIP 可分析", na.Ok, na.Error);
            Eq("嵌套 ZIP 的 pack_format", na.Format.PackFormat, 3);

            string nOut = Path.Combine(_root, "nested_out");
            var nPlan = PlanBuilder.Build(na, new ConvertOptions
            {
                Mode = OutputMode.Folder,
                OutputPath = nOut,
                TargetFormat = 15,
                TargetVersionLabel = "1.20 – 1.20.1",
                MigrateAssets = true,
            });
            var nRep = ConvertExecutor.Execute(nPlan, null);
            Check("嵌套 ZIP 转换成功", nRep.Success, nRep.Error);
            Check("剥离前缀后 pack.mcmeta 在根", File.Exists(Path.Combine(nOut, "pack.mcmeta")));
            Check("剥离前缀后纹理迁移到位", File.Exists(Path.Combine(nOut, "assets/minecraft/textures/block/dirt.png")));
        }

        private static void WriteEntry(ZipArchive zip, string name, string content)
        {
            var e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (var s = e.Open())
            {
                byte[] b = new UTF8Encoding(false).GetBytes(content);
                s.Write(b, 0, b.Length);
            }
        }

        /* ============================================================ */
        /* 9. 冲突                                                       */
        /* ============================================================ */

        private static void TestConflict()
        {
            string src = Path.Combine(_root, "conflict_src");
            Directory.CreateDirectory(Path.Combine(src, "assets/minecraft/textures/blocks"));
            Directory.CreateDirectory(Path.Combine(src, "assets/minecraft/textures/block"));
            File.WriteAllText(Path.Combine(src, "pack.mcmeta"),
                "{\"pack\":{\"pack_format\":3,\"description\":\"C\"}}", new UTF8Encoding(false));
            File.WriteAllBytes(Path.Combine(src, "assets/minecraft/textures/blocks/stone.png"), new byte[] { 1, 1, 1, 1 });
            File.WriteAllBytes(Path.Combine(src, "assets/minecraft/textures/block/stone.png"), new byte[] { 2, 2 });

            string outDir = Path.Combine(_root, "conflict_out");
            var analysis = PackAnalyzer.Analyze(src);
            var plan = PlanBuilder.Build(analysis, new ConvertOptions
            {
                Mode = OutputMode.Folder,
                OutputPath = outDir,
                TargetFormat = 15,
                TargetVersionLabel = "1.20 – 1.20.1",
                MigrateAssets = true,
            });

            var action = plan.Actions.FirstOrDefault(a => a.From == "assets/minecraft/textures/blocks/stone.png");
            Check("冲突文件被退回原路径", action != null && action.To == action.From,
                action != null ? "实际 " + action.To : "未找到");
            Check("冲突有说明", action != null && action.Notes.Any(n => n.Contains("已存在")));

            var report = ConvertExecutor.Execute(plan, null);
            Check("冲突情况下转换成功", report.Success, report.Error);
            Check("原有 textures/block/stone.png 未被覆盖",
                File.Exists(Path.Combine(outDir, "assets/minecraft/textures/block/stone.png")));
            Check("冲突文件保留在 textures/blocks 下",
                File.Exists(Path.Combine(outDir, "assets/minecraft/textures/blocks/stone.png")));
            Eq("原有文件字节未被改动",
                new FileInfo(Path.Combine(outDir, "assets/minecraft/textures/block/stone.png")).Length, 2L);
        }

        /* ============================================================ */
        /* 10. 边界                                                      */
        /* ============================================================ */

        private static void TestEdgeCases()
        {
            /* 不存在的路径 */
            var a1 = PackAnalyzer.Analyze(Path.Combine(_root, "no_such_thing"));
            Check("不存在的路径被拒绝", !a1.Ok && a1.Error.Contains("不存在"));

            /* 没有 pack.mcmeta 的文件夹 */
            string bare = Path.Combine(_root, "bare");
            Directory.CreateDirectory(bare);
            File.WriteAllText(Path.Combine(bare, "readme.txt"), "hi");
            var a2 = PackAnalyzer.Analyze(bare);
            Check("无 pack.mcmeta 时仍能分析但带告警", a2.Ok && !a2.PackMcmetaAtRoot);
            Check("给出了缺失告警", a2.Warnings.Any(w => w.Contains("pack.mcmeta")));

            var p2 = PlanBuilder.Build(a2, new ConvertOptions
            {
                Mode = OutputMode.Folder,
                OutputPath = Path.Combine(_root, "bare_out"),
                TargetFormat = 15,
                TargetVersionLabel = "1.20 – 1.20.1",
            });
            var r2 = ConvertExecutor.Execute(p2, null);
            Check("无 pack.mcmeta 时转换被拒绝", !r2.Success && r2.Error.Contains("pack.mcmeta"));

            /* 损坏的 pack.mcmeta */
            string broken = Path.Combine(_root, "broken");
            Directory.CreateDirectory(broken);
            File.WriteAllText(Path.Combine(broken, "pack.mcmeta"), "{ this is not json", new UTF8Encoding(false));
            var a3 = PackAnalyzer.Analyze(broken);
            Check("损坏的 pack.mcmeta 被识别", a3.Format != null && !string.IsNullOrEmpty(a3.Format.ParseError),
                a3.Format != null ? a3.Format.ParseError : "null");

            var p3 = PlanBuilder.Build(a3, new ConvertOptions
            {
                Mode = OutputMode.Folder,
                OutputPath = Path.Combine(_root, "broken_out"),
                TargetFormat = 46,
                TargetVersionLabel = "1.21.4",
                MigrateAssets = false,
            });
            var r3 = ConvertExecutor.Execute(p3, null);
            Check("损坏的 mcmeta 也能转换成可用文件", r3.Success, r3.Error);
            if (r3.Success)
            {
                JsonValue m; string err;
                Check("生成的是合法 JSON",
                    Json.TryParse(File.ReadAllText(Path.Combine(_root, "broken_out", "pack.mcmeta")), out m, out err), err);
                if (m != null) Eq("pack_format 写入正确", m.Get("pack").Get("pack_format").AsInt(), 46);
            }

            /* 输出与输入相同 */
            string same = Path.Combine(_root, "same_src");
            BuildLegacyPack(same);
            var a4 = PackAnalyzer.Analyze(same);
            var p4 = PlanBuilder.Build(a4, new ConvertOptions
            {
                Mode = OutputMode.Folder,
                OutputPath = same,
                TargetFormat = 15,
                TargetVersionLabel = "1.20 – 1.20.1",
            });
            var r4 = ConvertExecutor.Execute(p4, null);
            Check("拒绝原地覆盖", !r4.Success && r4.Error.Contains("相同"));
            Check("拒绝后源文件仍在", File.Exists(Path.Combine(same, "pack.mcmeta")));

            /* 输出在输入内部 */
            var p5 = PlanBuilder.Build(a4, new ConvertOptions
            {
                Mode = OutputMode.Folder,
                OutputPath = Path.Combine(same, "inner"),
                TargetFormat = 15,
                TargetVersionLabel = "1.20 – 1.20.1",
            });
            var r5 = ConvertExecutor.Execute(p5, null);
            Check("拒绝输出到输入内部", !r5.Success && r5.Error.Contains("内部"));

            /* 排除文件 */
            string exSrc = Path.Combine(_root, "exclude_src");
            BuildLegacyPack(exSrc);
            var a6 = PackAnalyzer.Analyze(exSrc);
            var opt6 = new ConvertOptions
            {
                Mode = OutputMode.Folder,
                OutputPath = Path.Combine(_root, "exclude_out"),
                TargetFormat = 15,
                TargetVersionLabel = "1.20 – 1.20.1",
                MigrateAssets = true,
                Excluded = new HashSet<string>(new[] { "assets/minecraft/textures/blocks/stone.png" }, StringComparer.OrdinalIgnoreCase),
            };
            var p6 = PlanBuilder.Build(a6, opt6);
            Check("被排除的文件标记为 Exclude",
                p6.Actions.Any(x => x.From == "assets/minecraft/textures/blocks/stone.png" && x.Kind == PlanActionKind.Exclude));
            var r6 = ConvertExecutor.Execute(p6, null);
            Check("带排除的转换成功", r6.Success, r6.Error);
            Check("被排除的文件没出现在输出里",
                !File.Exists(Path.Combine(_root, "exclude_out", "assets/minecraft/textures/blocks/stone.png")));
            Check("被排除的文件也没有被改名后写出",
                !File.Exists(Path.Combine(_root, "exclude_out", "assets/minecraft/textures/block/stone.png")));

            /* 覆盖已存在的输出目录 */
            string ovSrc = Path.Combine(_root, "overwrite_src");
            BuildLegacyPack(ovSrc);
            string ovOut = Path.Combine(_root, "overwrite_out");
            Directory.CreateDirectory(ovOut);
            File.WriteAllText(Path.Combine(ovOut, "stale.txt"), "old");
            var a7 = PackAnalyzer.Analyze(ovSrc);
            var p7 = PlanBuilder.Build(a7, new ConvertOptions
            {
                Mode = OutputMode.Folder,
                OutputPath = ovOut,
                TargetFormat = 15,
                TargetVersionLabel = "1.20 – 1.20.1",
                MigrateAssets = true,
            });
            var r7 = ConvertExecutor.Execute(p7, null);
            Check("可覆盖已存在的输出目录", r7.Success, r7.Error);
            Check("旧内容已被清除", !File.Exists(Path.Combine(ovOut, "stale.txt")));
            Check("新内容已就位", File.Exists(Path.Combine(ovOut, "pack.mcmeta")));
        }

        /* ============================================================ */
        /* 测试数据构造                                                  */
        /* ============================================================ */

        /// <summary>
        /// 造一个 1.12 风格的资源包：pack_format 3、textures/blocks 与
        /// textures/items 复数目录、en_US.lang 语言文件。
        /// </summary>
        /* ============================================================ */
        /* 11. 合并资源包                                                */
        /* ============================================================ */

        /// <summary>造一个与 BuildLegacyPack 部分同名、但有两个文件内容不同的包</summary>
        private static void BuildConflictPack(string root)
        {
            BuildLegacyPack(root);

            // 同名但内容不同 → 真冲突
            File.WriteAllText(Path.Combine(root, "assets", "minecraft", "models", "block", "stone.json"),
                "{\n  \"parent\": \"block/cube_all\",\n  \"textures\": {\"all\": \"blocks/stone_variant\"}\n}",
                new UTF8Encoding(false));

            File.WriteAllText(Path.Combine(root, "pack.mcmeta"),
                "{\n  \"pack\": {\n    \"pack_format\": 4,\n    \"description\": \"Conflict Pack\"\n  }\n}",
                new UTF8Encoding(false));

            // 只在这个包里存在的文件
            File.WriteAllBytes(Path.Combine(root, "assets", "minecraft", "textures", "blocks", "only_here.png"),
                MakeTinyPng());
        }

        private static void TestMerge()
        {
            string a = Path.Combine(_root, "merge_a");
            string b = Path.Combine(_root, "merge_b");
            BuildLegacyPack(a);
            BuildConflictPack(b);

            var sources = new List<MergeSource>
            {
                new MergeSource { Path = a, Label = "A" },
                new MergeSource { Path = b, Label = "B" },
            };

            var plan = PackMerger.Plan(sources, null);

            Check("两个来源都被识别", sources.All(s => s.Enabled),
                string.Join(";", sources.Select(s => s.Label + "=" + s.Enabled).ToArray()));

            /* 同名文件：stone.json 与 pack.mcmeta 内容不同，其余相同 */
            Eq("检测到 2 个内容不同的冲突", plan.RealConflictCount, 2);
            Check("冲突总数 >= 2", plan.ConflictCount >= 2, "实际 " + plan.ConflictCount);

            var rowConflict = plan.Conflicts.FirstOrDefault(c => c.RelPath.EndsWith("stone.json"));
            Check("stone.json 被识别为冲突", rowConflict != null);
            Check("stone.json 的内容被判为不同", rowConflict != null && !rowConflict.SameContent);
            Check("stone.json 涉及两个来源", rowConflict != null && rowConflict.Owners.Count == 2);

            var sameFile = plan.Conflicts.FirstOrDefault(c => c.RelPath.EndsWith("apple.png"));
            Check("内容相同的同名文件被判为 SameContent", sameFile != null && sameFile.SameContent);

            /* 默认按优先级：A 在前，所以 A 胜出 */
            var dStone = plan.Decisions.FirstOrDefault(d => d.RelPath.EndsWith("stone.json"));
            Eq("默认胜出者是优先级的第一个（A）", sources[dStone.WinnerIndex].Label, "A");

            /* 只在这个包里有的文件也要被收进来 */
            Check("B 独有的文件进入合并结果",
                plan.Decisions.Any(d => d.RelPath.EndsWith("only_here.png")));

            /* 改选 B 胜出 */
            var choices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            choices[rowConflict.RelPath] = 1;   // 下标 1 = B
            PackMerger.ApplyConflictChoices(plan, choices);
            Eq("改选后胜出者变为 B", sources[dStone.WinnerIndex].Label, "B");

            /* 排除 */
            choices[rowConflict.RelPath] = -1;
            PackMerger.ApplyConflictChoices(plan, choices);
            Check("可以排除某个冲突文件", dStone.Excluded);

            /* 恢复默认并执行 */
            choices[rowConflict.RelPath] = 0;
            PackMerger.ApplyConflictChoices(plan, choices);

            string outDir = Path.Combine(_root, "merge_out");
            plan.OutputPath = outDir;
            plan.Mode = StageOutput.Folder;
            plan.TargetFormat = 15;
            plan.NewDescription = "Merged by selftest";

            var report = PackMerger.Execute(plan, null);
            Check("合并执行成功", report.Ok, report.Error);
            Check("输出目录已生成", Directory.Exists(outDir));

            /* A 胜出，所以 stone.json 应是 A 的版本 */
            string stone = Path.Combine(outDir, "assets", "minecraft", "models", "block", "stone.json");
            Check("冲突文件写的是胜出来源的内容",
                File.Exists(stone) && File.ReadAllText(stone).Contains("blocks/stone\""),
                File.Exists(stone) ? File.ReadAllText(stone) : "(缺失)");

            Check("来源独有的文件被合并进来",
                File.Exists(Path.Combine(outDir, "assets", "minecraft", "textures", "blocks", "only_here.png")));

            /* pack.mcmeta 被改写 */
            JsonValue meta; string err;
            string metaText = File.ReadAllText(Path.Combine(outDir, "pack.mcmeta"));
            Check("合并后的 pack.mcmeta 是合法 JSON", Json.TryParse(metaText, out meta, out err), err);
            if (meta != null)
            {
                var pack = meta.Get("pack");
                Eq("pack_format 被设为 15", pack.Get("pack_format").AsInt(), 15);
                Eq("描述被替换", pack.Get("description").StringValue, "Merged by selftest");
            }

            /* 输出必须能被自己重新分析 */
            var re = PackAnalyzer.Analyze(outDir);
            Check("合并输出可被重新分析", re.Ok, re.Error);

            /* 不允许把输出写进输入目录 */
            string bad = StagedWriter.CheckOutput(a, Path.Combine(a, "sub"));
            Check("拒绝把输出写进输入目录内部", bad != null);
            string same = StagedWriter.CheckOutput(a, a);
            Check("拒绝输出路径等于输入路径", same != null);

            foreach (var s in sources) if (s.Analysis != null) s.Analysis.Cleanup();
        }

        /* ============================================================ */
        /* 12. PNG 编码器                                                */
        /* ============================================================ */

        private static void TestPngWriter()
        {
            /* 手写的 CRC32 / zlib 必须能被真正的解码器接受 —— 用 GDI+ 反过来读 */
            byte[] png = IconMaker.Make(32, IconPattern.Solid, Color.FromArgb(255, 0, 0), Color.Black, 8, null);

            Check("PNG 文件头正确",
                png.Length > 8 && png[0] == 0x89 && png[1] == 0x50 && png[2] == 0x4E && png[3] == 0x47);

            string path = Path.Combine(_root, "icon_solid.png");
            File.WriteAllBytes(path, png);

            bool decoded = false;
            int w = 0, h = 0;
            Color first = Color.Empty;
            try
            {
                using (var img = Image.FromFile(path))
                {
                    decoded = true;
                    w = img.Width; h = img.Height;
                    using (var bmp = new Bitmap(img))
                        first = bmp.GetPixel(0, 0);
                }
            }
            catch (Exception e)
            {
                Check("GDI+ 能解码我们生成的 PNG", false, e.Message);
            }

            Check("GDI+ 能解码我们生成的 PNG", decoded);
            Eq("宽 = 32", w, 32);
            Eq("高 = 32", h, 32);
            Check("像素是纯红且不透明",
                first.R == 255 && first.G == 0 && first.B == 0 && first.A == 255,
                first.ToString());

            /* 透明图 */
            byte[] tr = IconMaker.Make(8, IconPattern.Transparent, Color.White, Color.Black, 4, null);
            string tp = Path.Combine(_root, "icon_tr.png");
            File.WriteAllBytes(tp, tr);
            try
            {
                using (var img = Image.FromFile(tp))
                using (var bmp = new Bitmap(img))
                {
                    var c = bmp.GetPixel(4, 4);
                    /* Color.A 是 byte，和 int 直接比较会不相等，必须转 int */
                    Eq("透明图 alpha = 0", (int)c.A, 0);
                }
            }
            catch (Exception e)
            {
                Check("透明图可解码", false, e.Message);
            }

            /* 尺寸边界 */
            byte[] big = IconMaker.Make(1, IconPattern.Grid, Color.Gray, Color.Black, 1, null);
            Check("1×1 也能生成", big.Length > 0);
            byte[] huge = IconMaker.Make(9999, IconPattern.Checker, Color.Gray, Color.Black, 8, null);
            string hp = Path.Combine(_root, "icon_huge.png");
            File.WriteAllBytes(hp, huge);
            try
            {
                using (var img = Image.FromFile(hp))
                    Eq("超大尺寸被夹到 4096", img.Width, 4096);
            }
            catch (Exception e)
            {
                Check("超大图可解码", false, e.Message);
            }

            /* 从现有图片缩放 */
            string err;
            byte[] resized = IconMaker.ResizeToPng(path, 128, out err);
            Check("可以把图片缩放到指定尺寸", resized != null, err);
            if (resized != null)
            {
                string rp = Path.Combine(_root, "icon_resized.png");
                File.WriteAllBytes(rp, resized);
                using (var img = Image.FromFile(rp))
                {
                    Eq("缩放后宽 = 128", img.Width, 128);
                    Eq("缩放后高 = 128", img.Height, 128);
                }
            }
        }

        /* ============================================================ */
        /* 13. 新建资源包骨架                                             */
        /* ============================================================ */

        private static void TestScaffold()
        {
            /* --- 旧格式（扁平化之前）--- */
            string oldDir = Path.Combine(_root, "scaffold_old");
            var optOld = new ScaffoldOptions
            {
                OutputDir = oldDir,
                PackFormat = 3,
                Description = "Old Style Pack",
                Namespace = "mymod",
                WithSamples = true,
            };
            var r1 = PackScaffold.Create(optOld, null);
            Check("旧格式骨架创建成功", r1.Ok, r1.Error);
            Check("生成了 pack.mcmeta", File.Exists(Path.Combine(oldDir, "pack.mcmeta")));
            Check("旧格式用 textures/blocks",
                Directory.Exists(Path.Combine(oldDir, "assets", "mymod", "textures", "blocks")));
            Check("旧格式不建 items 目录",
                !Directory.Exists(Path.Combine(oldDir, "assets", "mymod", "items")));
            Check("默认生成了 pack.png", File.Exists(Path.Combine(oldDir, "pack.png")));

            var reOld = PackAnalyzer.Analyze(oldDir);
            Check("旧格式骨架可被重新分析", reOld.Ok, reOld.Error);
            Eq("分析得到 pack_format 3", reOld.Format.PackFormat, 3);
            Eq("描述写对了", reOld.Format.Description, "Old Style Pack");
            Eq("命名空间识别为 mymod", string.Join(",", reOld.Namespaces.ToArray()), "mymod");
            reOld.Cleanup();

            /* --- 新格式（1.21.4+，应有 items 目录）--- */
            string newDir = Path.Combine(_root, "scaffold_new");
            var optNew = new ScaffoldOptions
            {
                OutputDir = newDir,
                PackFormat = 46,
                Description = "Modern Pack",
                Namespace = "minecraft",
                WithSamples = true,
            };
            var r2 = PackScaffold.Create(optNew, null);
            Check("新格式骨架创建成功", r2.Ok, r2.Error);
            Check("新格式用 textures/block",
                Directory.Exists(Path.Combine(newDir, "assets", "minecraft", "textures", "block")));
            Check("新格式建了 items 目录",
                Directory.Exists(Path.Combine(newDir, "assets", "minecraft", "items")));

            var reNew = PackAnalyzer.Analyze(newDir);
            Check("新格式骨架可被重新分析", reNew.Ok, reNew.Error);
            Eq("分析得到 pack_format 46", reNew.Format.PackFormat, 46);
            reNew.Cleanup();

            /* --- 目录判定函数 --- */
            Check("pack_format 3 不用 items 目录", !PackScaffold.UsesItemsDir(3));
            Check("pack_format 46 用 items 目录", PackScaffold.UsesItemsDir(46));
            Eq("pack_format 3 的方块纹理目录", PackScaffold.BlockTextureDir(3), "blocks");
            Eq("pack_format 15 的方块纹理目录", PackScaffold.BlockTextureDir(15), "block");

            /* --- 拒绝把骨架建到已存在的非空目录上（会先备份）--- */
            string occupied = Path.Combine(_root, "scaffold_busy");
            Directory.CreateDirectory(occupied);
            File.WriteAllText(Path.Combine(occupied, "keepme.txt"), "original");
            var optBusy = new ScaffoldOptions { OutputDir = occupied, PackFormat = 15, WithSamples = false };
            var r3 = PackScaffold.Create(optBusy, null);
            Check("对已存在目录也能成功（先备份再替换）", r3.Ok, r3.Error);
            Check("原内容已被新骨架替换", !File.Exists(Path.Combine(occupied, "keepme.txt")));
            Check("替换后 pack.mcmeta 存在", File.Exists(Path.Combine(occupied, "pack.mcmeta")));
        }

        /* ============================================================ */
        /* 14. 批量重命名                                                */
        /* ============================================================ */

        private static void TestRename()
        {
            string src = Path.Combine(_root, "rename_src");
            BuildLegacyPack(src);

            /* --- 查找替换（只改文件名）--- */
            var rule = new RenameRule
            {
                Mode = RenameMode.FindReplace,
                Scope = RenameScope.FileNameOnly,
                Find = "stone",
                Replace = "rock",
            };
            var plan = PackRenamer.Plan(src, rule, null);

            Check("查找替换有命中", plan.ChangedCount > 0, plan.Describe());
            Check("计划可执行（无冲突、无非法）", plan.CanApply, plan.Describe());

            var hit = plan.Items.FirstOrDefault(i => i.From.EndsWith("stone.png"));
            Check("stone.png 被改名为 rock.png", hit != null && hit.To.EndsWith("rock.png"),
                hit != null ? hit.To : "(未命中)");
            Check("扩展名没有被改动", hit != null && hit.To.EndsWith(".png"));
            Check("目录部分没有被改动", hit != null && hit.To.StartsWith("assets/minecraft/textures/blocks/"));

            /* 只替文件名：stone.json 也应命中 */
            var jsonHit = plan.Items.FirstOrDefault(i => i.From.EndsWith("stone.json"));
            Check("models/block/stone.json 也被改名", jsonHit != null && jsonHit.To.EndsWith("rock.json"));

            /* --- 用正则 + 捕获组 --- */
            var reRule = new RenameRule
            {
                Mode = RenameMode.FindReplace,
                Scope = RenameScope.FileNameOnly,
                Find = @"^(\w+)$",
                Replace = "tex_$1",
                UseRegex = true,
            };
            var rePlan = PackRenamer.Plan(src, reRule, null);
            Check("正则捕获组生效",
                rePlan.Items.Any(i => i.To.EndsWith("tex_stone.png")),
                string.Join(",", rePlan.Items.Where(i => i.Changed).Take(3).Select(i => i.To).ToArray()));

            /* --- 非法正则要报错而不是崩 --- */
            var badRe = new RenameRule
            {
                Mode = RenameMode.FindReplace,
                Find = "([unclosed",
                Replace = "x",
                UseRegex = true,
            };
            var badPlan = PackRenamer.Plan(src, badRe, null);
            Check("非法正则不会抛异常，且被标为非法",
                badPlan.InvalidCount > 0, badPlan.Describe());

            /* --- 扩展名筛选 --- */
            var extRule = new RenameRule
            {
                Mode = RenameMode.Prefix,
                Prefix = "zz_",
                ExtFilter = "png",
            };
            var extPlan = PackRenamer.Plan(src, extRule, null);
            Check("扩展名筛选只命中 png",
                extPlan.Items.Where(i => i.Changed).All(i => i.To.EndsWith(".png")),
                string.Join(",", extPlan.Items.Where(i => i.Changed).Select(i => i.To).Take(3).ToArray()));
            /* 非 png 的必须一个都没碰 —— 直接按扩展名统计，不依赖具体某个文件名 */
            var changedExts = extPlan.Items.Where(i => i.Changed)
                .Select(i => Path.GetExtension(i.To).ToLowerInvariant())
                .Distinct().ToList();
            Check("改名命中的全都是 .png",
                changedExts.Count == 1 && changedExts[0] == ".png",
                "实际出现的扩展名：" + string.Join(",", changedExts.ToArray()));

            /*
             * Plan 只收录「通过筛选」的文件，所以计划里不会出现未改动的项。
             * 正确的验证方式是：计划中的文件必须是全部文件里扩展名为 png 的那个子集。
             */
            int allFiles = Directory.GetFiles(src, "*", SearchOption.AllDirectories).Length;
            int pngFiles = Directory.GetFiles(src, "*.png", SearchOption.AllDirectories).Length;

            Check("筛选后的计划只包含 png 文件", extPlan.Items.Count == pngFiles,
                "计划 " + extPlan.Items.Count + " 个，实际 png " + pngFiles + " 个，全部文件 " + allFiles + " 个");
            Check("筛选确实排除了非 png 文件", extPlan.Items.Count < allFiles,
                allFiles + " 个文件里筛出 " + extPlan.Items.Count + " 个");

            /* --- 加后缀要插在扩展名之前 --- */
            var sufRule = new RenameRule { Mode = RenameMode.Suffix, Suffix = "_v2" };
            var sufPlan = PackRenamer.Plan(src, sufRule, null);
            Check("后缀插在扩展名之前",
                sufPlan.Items.Any(i => i.To.EndsWith("_v2.png")),
                string.Join(",", sufPlan.Items.Where(i => i.Changed).Select(i => i.To).Take(3).ToArray()));

            /* --- 序号编号 --- */
            var numRule = new RenameRule
            {
                Mode = RenameMode.Number,
                NumberFormat = "{0:000}",
                NumberStart = 1,
                NumberStep = 1,
                ExtFilter = "png",
            };
            var numPlan = PackRenamer.Plan(src, numRule, null);
            Check("序号编号生成了 001 这样的名字",
                numPlan.Items.Any(i => i.To.EndsWith("001.png")),
                string.Join(",", numPlan.Items.Where(i => i.Changed).Select(i => i.To).Take(4).ToArray()));

            /* --- 冲突检测：把所有文件改成同一个名字 --- */
            var clashRule = new RenameRule
            {
                Mode = RenameMode.Number,
                NumberFormat = "same",     // 不接受 {0}，所有文件都变成 same.<ext>
                NumberStart = 1,
                NumberStep = 0,
            };
            var clashPlan = PackRenamer.Plan(src, clashRule, null);
            Check("同名冲突被检出", clashPlan.ConflictCount > 0, clashPlan.Describe());
            Check("有冲突时不允许执行", !clashPlan.CanApply);

            /* --- 实际执行一次 --- */
            var applyRule = new RenameRule
            {
                Mode = RenameMode.FindReplace,
                Scope = RenameScope.FileNameOnly,
                Find = "stone",
                Replace = "rock",
            };
            var applyPlan = PackRenamer.Plan(src, applyRule, null);
            int expectChanged = applyPlan.ChangedCount;

            string renameOut = Path.Combine(_root, "rename_out");

            /* 先验证「输出不能等于输入」的拦截 —— 这正是原地覆盖的坑 */
            var selfOut = PackRenamer.Execute(applyPlan, src, null);
            Check("拒绝把改名输出写回源目录", !selfOut.Ok, selfOut.Error);

            var report = PackRenamer.Execute(applyPlan, renameOut, null);
            Check("改名执行成功", report.Ok, report.Error);
            Check("输出目录已生成", Directory.Exists(report.OutputPath));
            Check("源目录依然存在且未被改动",
                File.Exists(Path.Combine(src, "assets", "minecraft", "textures", "blocks", "stone.png")));
            Eq("实际改名的数量与计划一致", report.FileCount, Directory.GetFiles(src, "*", SearchOption.AllDirectories).Length);

            Check("改名后的文件存在",
                File.Exists(Path.Combine(report.OutputPath, "assets", "minecraft", "textures", "blocks", "rock.png")));
            Check("原名字的文件已不在输出里",
                !File.Exists(Path.Combine(report.OutputPath, "assets", "minecraft", "textures", "blocks", "stone.png")));

            /* 文件总数不变 */
            int before = Directory.GetFiles(src, "*", SearchOption.AllDirectories).Length;
            int after = Directory.GetFiles(report.OutputPath, "*", SearchOption.AllDirectories).Length;
            Eq("改名前后文件总数一致", after, before);

            /* 内容没被改坏 */
            string rockPath = Path.Combine(report.OutputPath, "assets", "minecraft", "textures", "blocks", "rock.png");
            string stonePath = Path.Combine(src, "assets", "minecraft", "textures", "blocks", "stone.png");
            Check("改名后内容字节一致",
                File.ReadAllBytes(rockPath).SequenceEqual(File.ReadAllBytes(stonePath)));

            /* 输出仍是一个合法资源包 */
            var re = PackAnalyzer.Analyze(report.OutputPath);
            Check("改名输出仍是可识别的资源包", re.Ok && re.PackMcmetaAtRoot, re.Error);
            re.Cleanup();
        }


        /* ============================================================ */
        /* 15. NBT 数据模型与 SNBT                                       */
        /* ============================================================ */

        /// <summary>造一棵覆盖全部类型的 NBT 树，二进制与文本两节测试共用</summary>
        private static NbtCompound MakeSampleNbt()
        {
            var root = new NbtCompound("");
            root.Add(new NbtByte("byteVal", -12));
            root.Add(new NbtShort("shortVal", -3000));
            root.Add(new NbtInt("intVal", -70000));
            root.Add(new NbtLong("longVal", long.MinValue));
            root.Add(new NbtFloat("floatVal", 1.5f));
            root.Add(new NbtDouble("doubleVal", -2.25));
            root.Add(new NbtString("str", "中文 \"quoted\" \n 换行"));
            root.Add(new NbtByteArray("bytes", new byte[] { 0, 127, 255 }));
            root.Add(new NbtIntArray("ints", new int[] { -1, 0, int.MaxValue }));
            root.Add(new NbtLongArray("longs", new long[] { long.MinValue, long.MaxValue }));
            root.Add(new NbtShortArray("shorts", new short[] { -1, 2 }));
            root.Add(new NbtList("emptyList"));
            var list = new NbtList("list", NbtType.Int);
            list.Add(new NbtInt("", 1));
            list.Add(new NbtInt("", 2));
            root.Add(list);
            var inner = new NbtCompound("inner");
            inner.Add(new NbtString("name", "Steve"));
            inner.Add(new NbtByte("hardcore", 1));
            root.Add(inner);
            return root;
        }

        private static void TestNbtModel()
        {
            var root = MakeSampleNbt();

            /* ---- 深拷贝是「深」的 ---- */
            var clone = (NbtCompound)root.Clone();
            Check("克隆出来的树与原树相等", root.ValueEquals(clone));
            ((NbtByte)clone.Get("byteVal")).Value = 99;
            Check("改克隆不影响原树", !root.ValueEquals(clone));
            Eq("原树的字节值没被改动", ((NbtByte)root.Get("byteVal")).Value, (sbyte)-12);

            /* ---- 列表按第一个元素定型 ---- */
            var typed = new NbtList("l");
            Eq("空列表的元素类型是 TAG_End", typed.ElementType, NbtType.End);
            typed.Add(new NbtInt("", 1));
            Eq("加入整数后元素类型变成 TAG_Int", typed.ElementType, NbtType.Int);
            bool rejected = false;
            try { typed.Add(new NbtString("", "x")); }
            catch (FormatException) { rejected = true; }
            Check("往整数列表里加字符串会被拒绝", rejected);

            /* ---- SNBT 往返 ---- */
            string compact = NbtSnbt.Format(root);
            NbtTag parsed;
            string err;
            Check("紧凑 SNBT 能被解析", NbtSnbt.TryParse(compact, out parsed, out err), err);
            Check("紧凑 SNBT 往返后完全相等", parsed != null && root.ValueEquals(parsed));

            string pretty = NbtSnbt.FormatPretty(root);
            NbtTag parsed2;
            Check("美化 SNBT 能被解析", NbtSnbt.TryParse(pretty, out parsed2, out err), err);
            Check("美化 SNBT 往返后完全相等", parsed2 != null && root.ValueEquals(parsed2));

            /* ---- 类型后缀 ---- */
            NbtTag t;
            Check("1b 解析成 TAG_Byte", NbtSnbt.TryParse("1b", out t, out err) && t.Type == NbtType.Byte, err);
            Check("1s 解析成 TAG_Short", NbtSnbt.TryParse("1s", out t, out err) && t.Type == NbtType.Short, err);
            Check("1 解析成 TAG_Int", NbtSnbt.TryParse("1", out t, out err) && t.Type == NbtType.Int, err);
            Check("1L 解析成 TAG_Long", NbtSnbt.TryParse("1L", out t, out err) && t.Type == NbtType.Long, err);
            Check("1.0f 解析成 TAG_Float", NbtSnbt.TryParse("1.0f", out t, out err) && t.Type == NbtType.Float, err);
            Check("1.0 解析成 TAG_Double", NbtSnbt.TryParse("1.0", out t, out err) && t.Type == NbtType.Double, err);
            Check("裸词解析成 TAG_String", NbtSnbt.TryParse("hello", out t, out err) && t.Type == NbtType.String, err);
            Check("[B;1,2,3] 解析成字节数组", NbtSnbt.TryParse("[B;1,2,3]", out t, out err) && t.Type == NbtType.ByteArray, err);
            Check("[I;1,2] 解析成整数数组", NbtSnbt.TryParse("[I;1,2]", out t, out err) && t.Type == NbtType.IntArray, err);
            Check("[L;1L] 解析成长整数数组", NbtSnbt.TryParse("[L;1L]", out t, out err) && t.Type == NbtType.LongArray, err);
            Check("long 最小值能解析", NbtSnbt.TryParse("-9223372036854775808L", out t, out err) && ((NbtLong)t).Value == long.MinValue, err);

            /* ---- 错误要报出来而不是吞掉 ---- */
            Check("括号不配对会报错", !NbtSnbt.TryParse("{a:1", out t, out err));
            Check("缺冒号会报错", !NbtSnbt.TryParse("{a 1}", out t, out err));
            Check("尾部多余内容会报错", !NbtSnbt.TryParse("{a:1} x", out t, out err));
            Check("字节越界会报错", !NbtSnbt.TryParse("300b", out t, out err));

            /* ---- 根名 ---- */
            NbtCompound r2;
            string rn2;
            Check("带名字的根名能读出来", NbtSnbt.TryParseRoot("Level {a:1}", out r2, out rn2, out err) && rn2 == "Level", err);
            Check("非复合根会被包一层", NbtSnbt.TryParseRoot("[1,2]", out r2, out rn2, out err) && r2.Children.Count == 1, err);

            /* ---- 路径 ---- */
            Eq("根路径", "/", NbtWalk.PathOf(root, root));
            Eq("复合子项路径", "/inner/name", NbtWalk.PathOf(root, ((NbtCompound)root.Get("inner")).Get("name")));
            Eq("列表元素路径", "/list[1]", NbtWalk.PathOf(root, ((NbtList)root.Get("list")).Items[1]));
            int nodes = 0;
            foreach (var spot in NbtWalk.All(root)) nodes++;
            Eq("遍历出的节点数", nodes, NbtEditOps.CountNodes(root));

            /* ---- 查找 ---- */
            var rule = new NbtSearchRule();
            rule.NamePattern = "*Val";
            rule.NameMode = NbtMatchMode.Wildcard;
            Eq("通配符 *Val 命中 6 项（byte/short/int/long/float/double）", NbtSearch.FindAll(root, rule, 0).Count, 6);
            rule.NamePattern = "^byte.*$";
            rule.NameMode = NbtMatchMode.Regex;
            Eq("正则 ^byte.*$ 命中 2 项", NbtSearch.FindAll(root, rule, 0).Count, 2);
            rule.NamePattern = "";
            rule.NameMode = NbtMatchMode.Wildcard;
            rule.FilterByType = true;
            rule.Type = NbtType.String;
            Eq("按类型找字符串命中 2 项", NbtSearch.FindAll(root, rule, 0).Count, 2);
            rule.FilterByType = false;
            rule.ValueCompare = NbtValueCompare.Greater;
            rule.ValueText = "0";
            Eq("按值大于 0 命中 4 项", NbtSearch.FindAll(root, rule, 0).Count, 4);
            rule.ValueCompare = NbtValueCompare.Contains;
            rule.ValueText = "Steve";
            Eq("按值包含 Steve 命中 1 项", NbtSearch.FindAll(root, rule, 0).Count, 1);

            /* ---- 撤销重做 ---- */
            var history = new NbtHistory();
            var doc = MakeSampleNbt();
            history.Push(doc, "");
            ((NbtByte)doc.Get("byteVal")).Value = 5;
            NbtCompound cur = doc;
            string curName = "";
            Check("撤销能把值还原", history.Undo(ref cur, ref curName) && ((NbtByte)cur.Get("byteVal")).Value == (sbyte)-12);
            Check("重做能回到改动后", history.Redo(ref cur, ref curName) && ((NbtByte)cur.Get("byteVal")).Value == (sbyte)5);

            var history2 = new NbtHistory();
            history2.Push(cur, "");
            history2.PopLast();
            Check("PopLast 之后撤销栈为空", !history2.CanUndo && !history2.CanRedo);

            /* ---- 编辑操作 ---- */
            var parent = new NbtCompound("p");
            parent.Add(new NbtInt("a", 1));
            Eq("重名会自动加后缀", NbtEditOps.UniqueName(parent, "a"), "a_2");
            Eq("没重名时不加后缀", NbtEditOps.UniqueName(parent, "b"), "b");

            var intList = new NbtList("l", NbtType.Int);
            intList.Add(new NbtInt("", 1));
            intList.Add(new NbtInt("", 2));
            parent.Add(intList);

            var spot2 = new NbtSpot { Tag = intList.Items[1], ParentList = intList, Index = 1 };
            Check("能把列表项上移", NbtEditOps.MoveUp(spot2));
            Eq("上移后的顺序", ((NbtInt)intList.Items[0]).Value, 2);
            Check("能把列表项下移", NbtEditOps.MoveDown(spot2));
            Eq("下移后的顺序", ((NbtInt)intList.Items[0]).Value, 1);
            Check("已在最前面时上移返回 false", !NbtEditOps.MoveUp(new NbtSpot { Tag = intList.Items[0], ParentList = intList, Index = 0 }));

            string reason;
            Check("整数能粘进整数列表", NbtEditOps.CanPaste(intList, new NbtInt("", 7), out reason), reason);
            Check("字符串不能粘进整数列表", !NbtEditOps.CanPaste(intList, new NbtString("", "x"), out reason));
            Check("标量上不能粘贴", !NbtEditOps.CanPaste(new NbtInt("s", 1), new NbtInt("", 1), out reason));

            /* ---- JSON 导出 ---- */
            string json = NbtJson.Write(root, true);
            JsonValue jv;
            string jerr;
            Check("导出的 JSON 能被自家解析器读回", Json.TryParse(json, out jv, out jerr), jerr);
            Check("JSON 里数字仍是数字", jv != null && jv.Get("intVal").IsNumber);
            Check("JSON 里数组仍是数组", jv != null && jv.Get("bytes").IsArray);
        }

        /* ============================================================ */
        /* 16. NBT 二进制读写                                            */
        /* ============================================================ */

        private static void TestNbtIo()
        {
            string dir = Path.Combine(_root, "nbt");
            Directory.CreateDirectory(dir);

            var root = MakeSampleNbt();
            var doc = new NbtDocument
            {
                Root = root,
                RootName = "Level",
                Compression = NbtCompression.GZip,
                LittleEndian = false,
                HasBedrockHeader = false,
                BedrockVersion = 9,
            };

            /* ---- 大端内存往返（不落盘） ---- */
            byte[] payload = NbtIo.Serialize(root, "Level", false);
            string rootName;
            var parsed = NbtIo.Deserialize(payload, 0, payload.Length, false, out rootName);
            Check("大端序列化后能解析回来", parsed != null);
            Eq("大端序列化保留了根名", rootName, "Level");
            Check("大端往返内容完全一致", parsed != null && parsed.ValueEquals(root));

            byte[] payloadLe = NbtIo.Serialize(root, "Level", true);
            var parsedLe = NbtIo.Deserialize(payloadLe, 0, payloadLe.Length, true, out rootName);
            Check("小端往返内容完全一致", parsedLe != null && parsedLe.ValueEquals(root));
            Check("大端与小端字节不同", !payload.SequenceEqual(payloadLe));

            /* ---- gzip 文件往返 ---- */
            string gz = Path.Combine(dir, "level_gzip.dat");
            NbtIo.Save(doc, gz, NbtCompression.GZip, false, false);
            var backGz = NbtIo.Load(gz);
            Check("gzip 文件往返内容一致", backGz.Root.ValueEquals(root));
            Eq("gzip 压缩方式被识别", backGz.Compression, NbtCompression.GZip);
            Check("gzip 没被误认成小端", !backGz.LittleEndian);
            byte[] gzHead = File.ReadAllBytes(gz);
            Check("gzip 文件头是 1F 8B", gzHead.Length > 2 && gzHead[0] == 0x1F && gzHead[1] == 0x8B);

            /* ---- zlib 文件往返（.NET 的 DeflateStream 不认 zlib 头，容易踩坑） ---- */
            string zl = Path.Combine(dir, "level_zlib.dat");
            NbtIo.Save(doc, zl, NbtCompression.Zlib, false, false);
            var backZl = NbtIo.Load(zl);
            Check("zlib 文件往返内容一致", backZl.Root.ValueEquals(root));
            Eq("zlib 压缩方式被识别", backZl.Compression, NbtCompression.Zlib);
            byte[] zlHead = File.ReadAllBytes(zl);
            Check("zlib 文件头是 78 9C", zlHead.Length > 2 && zlHead[0] == 0x78 && zlHead[1] == 0x9C);

            /* ---- 不压缩 ---- */
            string raw = Path.Combine(dir, "level_raw.dat");
            NbtIo.Save(doc, raw, NbtCompression.None, false, false);
            var backRaw = NbtIo.Load(raw);
            Check("不压缩往返内容一致", backRaw.Root.ValueEquals(root));
            Eq("不压缩被识别", backRaw.Compression, NbtCompression.None);

            /* ---- 基岩版：8 字节头 + 小端 ---- */
            string bed = Path.Combine(dir, "bedrock_level.dat");
            var bedrockDoc = new NbtDocument
            {
                Root = root,
                RootName = "Level",
                Compression = NbtCompression.None,
                LittleEndian = true,
                HasBedrockHeader = true,
                BedrockVersion = 9,
            };
            NbtIo.Save(bedrockDoc, bed, NbtCompression.None, true, true);
            var backBed = NbtIo.Load(bed);
            Check("基岩版文件往返内容一致", backBed.Root.ValueEquals(root));
            Check("基岩版被认出小端", backBed.LittleEndian);
            Check("基岩版 8 字节头被认出", backBed.HasBedrockHeader);
            Eq("基岩版版本号保留", backBed.BedrockVersion, 9);

            /* ---- 写坏数据要报错，不能死循环 ---- */
            bool threw = false;
            try { NbtIo.Deserialize(new byte[] { 0x0A, 0x00 }, 0, 2, false, out rootName); }
            catch (Exception) { threw = true; }
            Check("截断的数据会抛异常", threw);

            threw = false;
            try
            {
                /* 声称有一个 300 字节长的字符串，实际没有 */
                byte[] bad = new byte[] { 0x0A, 0x00, 0x00, 0x08, 0x00, 0x01, 0x61, 0x01, 0x2C, 0x00 };
                NbtIo.Deserialize(bad, 0, bad.Length, false, out rootName);
            }
            catch (Exception) { threw = true; }
            Check("字符串长度越界会抛异常", threw);

            /* ---- 手工构造的已知字节：证明不是「自己写自己读」的假通过 ---- */
            /* 0A 00 01 61  => 根：TAG_Compound，名字长度 1，名字 "a"
               09 00 01 62  => 子项：TAG_List，名字长度 1，名字 "b"
               01 00 00 00 02 => 元素类型 TAG_Byte，2 个元素
               05 07        => 两个字节 5 和 7
               00           => 根结束 */
            byte[] known = new byte[]
            {
                0x0A, 0x00, 0x01, 0x61,
                0x09, 0x00, 0x01, 0x62,
                0x01, 0x00, 0x00, 0x00, 0x02,
                0x05, 0x07,
                0x00
            };
            string knownName;
            var knownTree = NbtIo.Deserialize(known, 0, known.Length, false, out knownName);
            Eq("手工样本的根名", knownName, "a");
            Check("手工样本的根是复合标签", knownTree != null && knownTree.Children.Count == 1);
            if (knownTree != null && knownTree.Children.Count == 1)
            {
                var kl = knownTree.Children[0] as NbtList;
                Check("手工样本的子项是列表", kl != null);
                if (kl != null)
                {
                    Eq("手工样本的列表元素类型", kl.ElementType, NbtType.Byte);
                    Eq("手工样本的列表长度", kl.Items.Count, 2);
                    Eq("手工样本的第一个元素", ((NbtByte)kl.Items[0]).Value, (sbyte)5);
                    Eq("手工样本的第二个元素", ((NbtByte)kl.Items[1]).Value, (sbyte)7);
                }
            }

            /* ---- SNBT 与二进制串起来：文本改完存成文件再读 ---- */
            NbtCompound textRoot;
            string textRootName;
            string terr;
            Check("SNBT 能解析成根", NbtSnbt.TryParseRoot("Level {Data:{version:19133},List:[1L,2L]}", out textRoot, out textRootName, out terr), terr);
            string textFile = Path.Combine(dir, "from_text.dat");
            var textDoc = new NbtDocument
            {
                Root = textRoot,
                RootName = textRootName,
                Compression = NbtCompression.GZip,
                LittleEndian = false,
            };
            NbtIo.Save(textDoc, textFile, NbtCompression.GZip, false, false);
            var backText = NbtIo.Load(textFile);
            Eq("文本转文件后根名保留", backText.RootName, "Level");
            Check("文本转文件后结构与文本一致", backText.Root.ValueEquals(textRoot));
        }


        private static void BuildLegacyPack(string root)
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);

            string mc = Path.Combine(root, "assets", "minecraft");
            string mymod = Path.Combine(root, "assets", "mymod");

            Directory.CreateDirectory(Path.Combine(mc, "textures", "blocks"));
            Directory.CreateDirectory(Path.Combine(mc, "textures", "items"));
            Directory.CreateDirectory(Path.Combine(mc, "textures", "gui"));
            Directory.CreateDirectory(Path.Combine(mc, "models", "block"));
            Directory.CreateDirectory(Path.Combine(mc, "lang"));
            Directory.CreateDirectory(Path.Combine(mc, "sounds"));
            Directory.CreateDirectory(Path.Combine(mymod, "textures", "blocks"));

            File.WriteAllText(Path.Combine(root, "pack.mcmeta"),
                "{\n  \"pack\": {\n    \"pack_format\": 3,\n    \"description\": \"Legacy 1.12 Pack\"\n  }\n}",
                new UTF8Encoding(false));

            byte[] png = MakeTinyPng();
            File.WriteAllBytes(Path.Combine(mc, "textures", "blocks", "stone.png"), png);
            File.WriteAllBytes(Path.Combine(mc, "textures", "blocks", "dirt.png"), png);
            File.WriteAllBytes(Path.Combine(mc, "textures", "items", "apple.png"), png);
            File.WriteAllBytes(Path.Combine(mc, "textures", "gui", "widgets.png"), png);
            File.WriteAllBytes(Path.Combine(mymod, "textures", "blocks", "ore.png"), png);

            File.WriteAllText(Path.Combine(mc, "models", "block", "stone.json"),
                "{\n  \"parent\": \"block/cube_all\",\n  \"textures\": {\"all\": \"blocks/stone\"}\n}", new UTF8Encoding(false));

            File.WriteAllText(Path.Combine(mc, "lang", "en_US.lang"),
                "block.stone=Stone\n" +
                "tile.grass=Grass Block\n" +
                "item.apple=Apple\n" +
                "gui.done=Done\n" +
                "itemGroup.buildingBlocks=Building Blocks\n",
                new UTF8Encoding(false));

            File.WriteAllBytes(Path.Combine(mc, "sounds", "click.ogg"), new byte[] { 79, 103, 103, 83 });

            // 一个 64 字节的假纹理，用于校验字节级完整性
            var big = new byte[64];
            for (int i = 0; i < big.Length; i++) big[i] = (byte)(i * 3 & 0xFF);
            File.WriteAllBytes(Path.Combine(mc, "textures", "blocks", "texture_bytes.png"), big);
        }

        private static byte[] MakeTinyPng()
        {
            // 1x1 透明 PNG
            return new byte[]
            {
                0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A,
                0x00,0x00,0x00,0x0D,0x49,0x48,0x44,0x52,
                0x00,0x00,0x00,0x01,0x00,0x00,0x00,0x01,
                0x08,0x06,0x00,0x00,0x00,0x1F,0x15,0xC4,
                0x89,0x00,0x00,0x00,0x0A,0x49,0x44,0x41,
                0x54,0x78,0x9C,0x63,0x00,0x01,0x00,0x00,
                0x05,0x00,0x01,0x0D,0x0A,0x2D,0xB4,0x00,
                0x00,0x00,0x00,0x49,0x45,0x4E,0x44,0xAE,
                0x42,0x60,0x82
            };
        }
    }
}
