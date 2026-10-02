using System;
using System.Collections.Generic;
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
