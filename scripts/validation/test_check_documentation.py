"""Regression checks for deletion detection and intentional historical exclusions."""

import importlib.util
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("documentation", Path(__file__).with_name("check-documentation.py"))
documentation = importlib.util.module_from_spec(spec)
spec.loader.exec_module(documentation)


class DocumentationTests(unittest.TestCase):
    def setUp(self):
        self.workspace = tempfile.TemporaryDirectory()
        self.addCleanup(self.workspace.cleanup)
        self.root = Path(self.workspace.name)
        self.write("AGENTS.md", "Rules")
        self.write("README.md", "Project")
        self.write("docs/development/current-state.md", "Current")

    def write(self, path, content):
        target = self.root / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(content, encoding="utf-8")

    def errors(self):
        return documentation.check(self.root)[1]

    def test_deleted_relative_target_fails_including_evidence_records(self):
        self.write("docs/development/handoffs/task.md", "[next](../deleted.md)")
        self.assertEqual(self.errors(), ["docs/development/handoffs/task.md: missing link ../deleted.md"])

    def test_unicode_encoded_target_with_fragment_and_reference_link(self):
        self.write("docs/说明.md", "# title")
        self.write("README.md", "[guide][manual]\n\n[manual]: docs/%E8%AF%B4%E6%98%8E.md#title")
        self.assertEqual(self.errors(), [])

    def test_old_archive_contents_and_examples_are_excluded(self):
        self.write("docs/archive/old.md", "[old](missing.md)")
        self.write("README.md", "```markdown\n[example](missing.md)\n```\n`[inline](absent.md)`")
        self.assertEqual(self.errors(), [])

    def test_current_link_to_deleted_archive_target_still_fails(self):
        self.write("README.md", "[evidence](docs/archive/deleted.md)")
        self.assertEqual(self.errors(), ["README.md: missing link docs/archive/deleted.md"])

    def test_remote_url_and_anchor_are_not_local_files(self):
        self.write("README.md", "[web](https://example.com/docs) [email](mailto:dev@example.com) [section](#title)")
        self.assertEqual(self.errors(), [])

    def test_state_limit_is_inclusive(self):
        self.write("docs/development/current-state.md", "line\n" * 120)
        self.assertEqual(self.errors(), [])
        self.write("docs/development/current-state.md", "line\n" * 121)
        self.assertTrue(any("exceeds 120" in error for error in self.errors()))


if __name__ == "__main__":
    unittest.main()
