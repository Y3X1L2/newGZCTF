"""真实临时目录/Git 仓库回归；不把 fixture 当作项目文档提交。"""

import importlib.util
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

import documentation as docs

TOOLS = Path(__file__).parent
spec = importlib.util.spec_from_file_location("check_documentation", TOOLS / "check-documentation.py")
checker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(checker)


class Fixture(unittest.TestCase):
    def setUp(self):
        self.workspace = tempfile.TemporaryDirectory()
        self.addCleanup(self.workspace.cleanup)
        self.root = Path(self.workspace.name)
        self.write("AGENTS.md", "# 规则\n")
        self.write("README.md", "# 项目\n")
        self.write("docs/README.md", "# 导航\n")
        self.write("docs/current-state.md", "# 当前事实\n")
        self.modules = [self.module("training", "培训课程", ["src/GZCTF/Modules/Training/**"]),
                        self.module("runtime", "运行底座", ["src/GZCTF/Modules/Runtime/**", "src/GZCTF.Agent/**"])]
        for module in self.modules:
            self.write(module["overview"], "# 用途\n")
            self.write(module["development"], "# 开发\n")
        self.write("src/GZCTF/Modules/Training/Service.cs", "first\nsecond\n")
        self.write("src/GZCTF/Modules/Runtime/Queue.cs", "queue\n")
        self.write("src/GZCTF.Agent/Program.cs", "agent\n")
        self.save_catalog()
        self.write("docs/modules/README.md", docs.generated_index(self.modules))

    def module(self, module_id, title, code):
        return {"id": module_id, "title": title, "overview": f"docs/modules/{module_id}/README.md",
                "development": f"docs/modules/{module_id}/development.md", "code": code,
                "docs": [f"docs/modules/{module_id}/**"]}

    def write(self, path, content):
        target = self.root / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(content, encoding="utf-8")
        return target

    def save_catalog(self):
        self.write(docs.CATALOG_PATH, json.dumps({"version": 1, "modules": self.modules}, ensure_ascii=False))

    def run_tool(self, name, *args):
        return subprocess.run([sys.executable, "-X", "utf8", str(TOOLS / name), "--root", str(self.root), *args],
                              capture_output=True, encoding="utf-8")


class LinkTests(Fixture):
    def errors(self):
        return docs.check_links(self.root)[1]

    def test_valid_full_check_and_unicode_reference_destination(self):
        self.write("docs/说明 (副本).md", "# 中文 标题\n# API `Token` 与身份\n# 中文 标题\n<a id='自定义'></a>\n")
        self.write("README.md", "# 项目\n[参考][说明]\n\n[说明]: <docs/%E8%AF%B4%E6%98%8E%20(%E5%89%AF%E6%9C%AC).md#中文-标题-1> \"说明\"\n"
                   "[API](<docs/说明 (副本).md#api-token-与身份>)\n[锚点](<docs/说明 (副本).md#自定义>)\n"
                   "[源码](src/GZCTF/Modules/Training/Service.cs#L1-L2)\n[本页](#项目)\n")
        self.assertEqual(checker.check(self.root)[1:], ([], []))

    def test_non_angle_balanced_parentheses_and_html_entities(self):
        self.write("docs/guide(v2).md", "# A &amp; B\n")
        self.write("README.md", "[guide](docs/guide(v2).md#a--b)")
        self.assertEqual(self.errors(), [])

    def test_missing_local_file_image_reference_and_anchor_report_location(self):
        self.write("README.md", "# 项目\n[bad](docs/missing.md)\n![img](absent.png)\n[ref]: docs/absent.md\n[anchor](#没有)\n")
        errors = self.errors()
        self.assertEqual(len(errors), 4)
        self.assertTrue(any("README.md:2" in error and "missing.md" in error for error in errors))
        self.assertTrue(any("锚点不存在" in error for error in errors))

    def test_external_urls_and_fenced_inline_indented_examples_are_excluded(self):
        self.write("README.md", "[web](https://example.com/nope#missing) [email](mailto:dev@example.com)\n"
                   "[protocol](//example.com/nope)\n```markdown\n[example](missing.md)\n```\n"
                   "~~~markdown\n[example](missing.md)\n~~~\n`[inline](absent.md)`\n"
                   "    [example](missing.md)\n<!-- [example](missing.md) -->\n")
        self.assertEqual(self.errors(), [])

    def test_archive_is_never_read_even_when_linked_with_anchor(self):
        path = self.root / "docs/archive/old.md"
        path.parent.mkdir(parents=True)
        path.write_bytes(b"\xff\xfe\x00")
        self.write("README.md", "[历史](docs/archive/old.md#旧标题)")
        self.assertEqual(self.errors(), [])
        self.write("README.md", "[历史](docs/archive/deleted.md)")
        self.assertTrue(any("deleted.md" in error for error in self.errors()))

    def test_history_records_have_links_checked_but_no_size_or_expiry_rules(self):
        self.write("docs/records/2000-01-01-task.md", "历史：进行中\n" * 500 + "[事实](../current-state.md#当前事实)\n")
        self.assertEqual(checker.check(self.root)[1:], ([], []))
        self.write("docs/records/2000-01-01-task.md", "[旧入口](missing.md)\n")
        self.assertTrue(any("records/2000" in error for error in self.errors()))

    def test_source_line_ranges_and_traversal(self):
        self.write("README.md", "[zero](src/GZCTF/Modules/Training/Service.cs#L3)\n"
                   "[reverse](src/GZCTF/Modules/Training/Service.cs#L2-L1)\n[escape](../outside.md)\n")
        errors = self.errors()
        self.assertEqual(len(errors), 3)
        self.assertTrue(any("越出仓库" in error for error in errors))

    def test_setext_and_duplicate_heading_slugs(self):
        anchors = docs.markdown_anchors("标题\n===\n## A\n## A\n## A-1\n## A\n## 名称：测试！\n")
        self.assertTrue({"标题", "a", "a-1", "a-1-1", "a-2", "名称测试"}.issubset(anchors))


class CatalogTests(Fixture):
    def test_schema_globs_and_directory_coverage(self):
        self.assertEqual(docs.check_catalog(self.root)[1], [])
        self.write("src/GZCTF/Modules/NewModule/Service.cs", "new\n")
        self.write("src/GZCTF.Agent/NewOperation.cs", "new\n")
        self.write("docs/modules/new-module/README.md", "# 新模块\n")
        self.modules[1]["code"] = ["src/GZCTF/Modules/Runtime/**", "src/GZCTF.Agent/Program.cs"]
        self.save_catalog()
        errors = docs.check_catalog(self.root)[1]
        self.assertTrue(any("NewModule/Service.cs" in error for error in errors))
        self.assertTrue(any("NewOperation.cs" in error for error in errors))
        self.assertTrue(any("模块目录未登记" in error for error in errors))

    def test_catalog_rejects_schema_path_and_glob_mistakes(self):
        for mutation in [lambda m: m.update(id="../training"), lambda m: m.update(code="src/**"),
                         lambda m: m.update(code=["../src/**"]), lambda m: m.update(code=["src\\**"]),
                         lambda m: m.update(code=["src/Train**/x"]), lambda m: m.update(overview="docs/other.md"),
                         lambda m: m.update(extra="unknown")]:
            with self.subTest(mutation=mutation):
                data = json.loads(json.dumps({"version": 1, "modules": self.modules}))
                mutation(data["modules"][0])
                self.assertTrue(docs.catalog_modules(data)[1])
        self.assertTrue(docs.catalog_modules({"version": True, "modules": self.modules})[1])
        self.assertTrue(docs.catalog_modules({"version": 1, "modules": self.modules * 2})[1])

    def test_stale_glob_missing_docs_and_doc_mapping_fail(self):
        self.modules[0]["code"].append("src/deleted/**")
        self.modules[0]["docs"] = ["docs/modules/training/README.md"]
        (self.root / self.modules[0]["development"]).unlink()
        self.save_catalog()
        errors = docs.check_catalog(self.root)[1]
        self.assertTrue(any("未匹配文件" in error for error in errors))
        self.assertTrue(any("文档不存在" in error for error in errors))
        self.assertTrue(any("docs 映射未包含" in error for error in errors))

    def test_globs_support_recursive_zero_levels_and_case_sensitive_paths(self):
        self.assertTrue(docs.glob_matches("src/GZCTF.Agent/Program.cs", "src/GZCTF.Agent/**/*.cs"))
        self.assertTrue(docs.glob_matches("src/GZCTF.Agent/Services/Run.cs", "src/GZCTF.Agent/**/*.cs"))
        self.assertFalse(docs.glob_matches("src/Training/sub/Run.cs", "src/Training/*.cs"))
        self.assertFalse(docs.glob_matches("src/training/Run.cs", "src/Training/**"))

    def test_platform_uses_architecture_pages_and_cannot_hide_unmapped_modules(self):
        platform = self.module("platform", "共享规则", ["src/**"])
        platform.update(overview="docs/architecture/README.md", development="docs/architecture/development.md",
                        docs=["docs/architecture/**"])
        self.modules.append(platform)
        self.write(platform["overview"], "# 架构\n")
        self.write(platform["development"], "# 共享开发\n")
        self.save_catalog()
        self.assertEqual(docs.check_catalog(self.root)[1], [])
        self.assertIn("[说明](../architecture/README.md)", docs.generated_index(self.modules))
        self.write("src/GZCTF/Modules/New/Service.cs", "new\n")
        self.assertTrue(any("New/Service.cs" in e for e in docs.check_catalog(self.root)[1]))
        (self.root / "src/GZCTF/Modules/New/Service.cs").unlink()
        self.write("src/GZCTF/Modules/ModuleAssemblyMarker.cs", "shared marker\n")
        self.assertEqual(docs.check_catalog(self.root)[1], [])

    def test_length_only_warns_and_generated_index_is_deterministic(self):
        self.write("docs/current-state.md", "line\n" * 121)
        self.write(self.modules[0]["development"], "line\n" * 201)
        files, errors, warnings = checker.check(self.root)
        self.assertFalse(errors)
        self.assertEqual(len(warnings), 2)
        self.assertEqual(docs.generated_index(self.modules), docs.generated_index(self.modules[::-1]))

    def test_generator_write_check_and_stale_index(self):
        index = self.root / "docs/modules/README.md"
        index.unlink()
        self.assertEqual(self.run_tool("generate-module-index.py", "--check").returncode, 1)
        self.assertEqual(self.run_tool("generate-module-index.py").returncode, 0)
        self.assertEqual(self.run_tool("generate-module-index.py", "--check").returncode, 0)
        self.write("docs/modules/README.md", "# 手改导航\n")
        self.assertTrue(any("与 catalog 不一致" in e for e in checker.check(self.root)[1]))


class ImpactTests(Fixture):
    def setUp(self):
        super().setUp()
        self.git("init", "--initial-branch=main")
        self.git("config", "user.email", "fixture@example.invalid")
        self.git("config", "user.name", "Fixture")
        self.commit("baseline")
        self.base = self.git("rev-parse", "HEAD").stdout.strip()

    def git(self, *args):
        return subprocess.run(["git", "-C", str(self.root), *args], check=True, capture_output=True, encoding="utf-8")

    def commit(self, message):
        self.git("add", ".")
        self.git("commit", "-m", message)

    def result(self):
        return docs.impact(self.root, self.base, self.modules)

    def body(self, modules="无", reason=""):
        return f"## 改动\n修复\n\n## 文档影响\n无需修改模块：{modules}\n无需修改理由：\n{reason}\n"

    def test_changed_code_requires_related_doc_or_specific_review(self):
        self.write("src/GZCTF/Modules/Training/Service.cs", "updated\n")
        self.commit("change training")
        result = self.result()
        self.assertTrue(docs.review_impact(result, self.body(), self.modules)[1])
        reason = "training: 仅拆分 `TrainingRuntimeService` 内部方法，额度和授权行为未变；现行开发说明仍准确，相关单元测试通过。"
        self.assertEqual(docs.review_impact(result, self.body("training", reason), self.modules)[1], [])
        actual_reason = "training: 本次仅为TrainingContainerQuotaTests中现有额度拒绝路径增加回归用例，接口、配置与运行行为未改变，已核对培训开发页。"
        self.assertEqual(docs.review_impact(result, self.body("training", actual_reason), self.modules)[1], [])
        self.write("docs/modules/runtime/development.md", "# 无关运行文档\n")
        self.commit("unrelated docs")
        self.assertTrue(docs.review_impact(self.result(), self.body(), self.modules)[1])
        self.write("docs/modules/training/development.md", "# 新开发说明\n")
        self.commit("related docs")
        self.assertEqual(docs.review_impact(self.result(), self.body(), self.modules)[1], [])

    def test_rename_reports_old_and_new_module_ownership(self):
        self.git("mv", "src/GZCTF/Modules/Training/Service.cs", "src/GZCTF/Modules/Runtime/Moved.cs")
        self.commit("rename")
        result = self.result()
        self.assertEqual(set(result["affected"]), {"training", "runtime"})
        self.assertEqual(result["changes"][0][0], "R100")
        self.assertIn("Service.cs → src/GZCTF/Modules/Runtime/Moved.cs", docs.impact_report(result))

    def test_deletion_uses_previous_catalog_when_current_mapping_removed(self):
        (self.root / "src/GZCTF/Modules/Training/Service.cs").unlink()
        self.modules[0]["code"] = ["src/GZCTF/Modules/Runtime/**"]
        self.save_catalog()
        self.commit("delete old training code and mapping")
        result = self.result()
        self.assertIn("training", result["affected"])
        self.assertEqual(result["affected"]["training"]["changes"], {("D", "src/GZCTF/Modules/Training/Service.cs", None)})

    def test_new_and_unmapped_code_visible_with_overlapping_module_mapping(self):
        self.modules[1]["code"].append("src/GZCTF/Modules/Training/**")
        self.save_catalog()
        self.write("src/GZCTF/Modules/Training/New.cs", "new\n")
        self.write("src/Other/New.cs", "new\n")
        self.commit("add shared and unmapped code")
        result = self.result()
        self.assertEqual(set(result["affected"]), {"training", "runtime"})
        self.assertEqual(result["unmapped"], [("A", "src/Other/New.cs")])
        self.assertIn("未映射源码", docs.impact_report(result))
        self.assertTrue(any("未映射源码" in e for e in docs.review_impact(result, self.body(), self.modules)[1]))

    def test_operational_scripts_under_docs_are_code_not_markdown(self):
        self.modules[1]["code"].append("docs/node-deployment/*.sh")
        self.save_catalog()
        self.write("docs/node-deployment/install.sh", "#!/bin/sh\nexit 0\n")
        self.commit("add operational script")
        result = self.result()
        self.assertIn("runtime", result["affected"])
        self.assertNotIn("docs/node-deployment/install.sh", result["changed_docs"])

    def test_merge_base_excludes_changes_only_on_base_branch(self):
        self.git("switch", "-c", "feature")
        self.write("src/GZCTF/Modules/Training/Service.cs", "feature\n")
        self.commit("feature")
        self.git("switch", "main")
        self.write("src/GZCTF/Modules/Runtime/Queue.cs", "main only\n")
        self.commit("advance main")
        self.git("switch", "feature")
        result = docs.impact(self.root, "main", self.modules)
        self.assertEqual(result["base"], self.base)
        self.assertEqual(set(result["affected"]), {"training"})

    def test_push_schedule_and_manual_only_report_without_pr(self):
        self.write("src/GZCTF/Modules/Training/Service.cs", "change\n")
        self.commit("change")
        event = self.write("event.json", json.dumps({"before": self.base}))
        report = self.root / "output/impact.md"
        result = self.run_tool("documentation-impact.py", "--base", self.base, "--event-file", str(event), "--report", str(report))
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("只读报告模式", report.read_text(encoding="utf-8"))
        pr = self.write("pr.json", json.dumps({"pull_request": {"body": self.body()}}))
        result = self.run_tool("documentation-impact.py", "--base", self.base, "--event-file", str(pr))
        self.assertEqual(result.returncode, 1)

    def test_cli_bytes_are_utf8_even_when_process_default_is_cp936(self):
        env = dict(os.environ, PYTHONUTF8="0", PYTHONIOENCODING="cp936")
        for tool, args in [("check-documentation.py", []), ("generate-module-index.py", ["--check"]),
                           ("documentation-impact.py", ["--base", self.base])]:
            with self.subTest(tool=tool):
                result = subprocess.run([sys.executable, str(TOOLS / tool), "--root", str(self.root), *args],
                                        capture_output=True, env=env)
                self.assertEqual(result.returncode, 0, result.stderr)
                self.assertRegex(result.stdout.decode("utf-8"), r"通过|报告")
        code = "import sys; from documentation import configure_cli_output; configure_cli_output(); sys.stderr.write('中文错误')"
        result = subprocess.run([sys.executable, "-c", code], cwd=TOOLS, capture_output=True, env=env)
        self.assertEqual(result.stderr.decode("utf-8"), "中文错误")
        with patch("documentation.sys.stdout", io.StringIO()), patch("documentation.sys.stderr", io.StringIO()):
            docs.configure_cli_output()  # 无 reconfigure 的捕获流同样可用。

    def test_pr_body_is_data_and_never_executes_commands(self):
        payload = "$(touch PWNED); `python -c 'open(\"PWNED\",\"w\")'`"
        body = self.write("pr-body.txt", self.body("training", "training: " + payload))
        event = self.write("pr-event.json", json.dumps({"pull_request": {"body": body.read_text(encoding="utf-8")}}))
        with patch("documentation.subprocess.run", wraps=subprocess.run) as run:
            self.run_tool("documentation-impact.py", "--base", self.base, "--event-file", str(event))
            for call in run.call_args_list:
                self.assertNotIn("shell", call.kwargs)
        self.assertFalse((self.root / "PWNED").exists())
        with self.assertRaises(ValueError):
            docs.diff_changes(self.root, "--output=PWNED")

    def test_unmapped_reason_cannot_bypass_new_module_structure_coverage(self):
        self.write("src/GZCTF/Modules/New/Service.cs", "new\n")
        self.commit("new module without catalog")
        reason = "unmapped: 仅添加 `NewService` 内部测试帮助器，现行业务行为未变，测试通过并已复核开发页。"
        pr = self.write("pr.json", json.dumps({"pull_request": {"body": self.body("unmapped", reason)}}))
        result = self.run_tool("documentation-impact.py", "--base", self.base, "--event-file", str(pr))
        self.assertEqual(result.returncode, 1)
        self.assertIn("Modules/Agent 文件未映射", result.stdout)


class ReviewTests(unittest.TestCase):
    def body(self, reason):
        return f"## 文档影响\n无需修改模块：training\n无需修改理由：\ntraining: {reason}\n"

    def test_generic_placeholder_duplicate_and_unknown_reasons_rejected(self):
        for reason in ["无需修改", "N/A", "纯代码", "待填写", "仅重构", "文档无变化" * 10,
                       "没有任何无意义变化因此无需填写任何事实", "仅修改内部实现，所有功能接口授权状态保持原样，已经验证无影响。"]:
            with self.subTest(reason=reason):
                self.assertTrue(docs.pr_review(self.body(reason), {"training"})[1])
        self.assertTrue(docs.pr_review("## 文档影响\n无需修改模块：unknown\n", {"training"})[1])
        self.assertTrue(docs.pr_review(self.body("具体原因" * 10) * 2, {"training"})[1])

    def test_commented_examples_cannot_satisfy_required_review(self):
        body = "## 文档影响\n无需修改模块：无\n无需修改理由：\n<!--\ntraining: 具体理由\n-->"
        self.assertEqual(docs.pr_review(body, {"training"}), ({}, []))
        self.assertTrue(docs.pr_review("<!--" + self.body("具体原因" * 10) + "-->", {"training"})[1])
        reason = "仅拆分 `TrainingRuntimeService` 内部方法，额度和授权未变；现行说明仍准确，相关单测通过。"
        self.assertTrue(docs.pr_review("```markdown\n" + self.body(reason) + "```", {"training"})[1])


if __name__ == "__main__":
    unittest.main()
