import io
import os
import shutil
import tempfile
import unittest

from levels_editor.expr import display_value
from levels_editor.lvl import LvlDocument, LvlParseError
from levels_editor.model import LevelsModel
from levels_editor.tcg import TcgDocument
from levels_editor.usrv import UsrvIndex

LVL = (
    "Version 1.0;\n\n\n"
    "Levels demo_lvl\n{\n"
    "\thcdps_all\n\t{\n"
    "\t\tIClampHi = p_hi;\n"
    "\t\tOPMode = \"VSIM\";\n"
    "\t\tVForce = 0V;\n"
    "\t}\n"
    "\tSequenceBreak 1mS;\n"
    "\tlcdps_all\n\t{\n"
    "\t\tVForce = 500mV;\n"
    "\t}\n"
    "\tSequenceBreak 0mS;\n"
    "}\n"
    "Levels other_lvl\n{\n}"  # no trailing newline, like the real files
)

TCG = (
    "Version 1.0;\n\n"
    "SpecificationSet demo_SPEC (lvl,hot)\n{\n"
    "\tVoltage p_hi = 0.5V,0.9V;\n"
    "\tDouble p_ratio = 2;\n"
    "}\n\n"
    "TestConditionGroup demo_TCG\n{\n"
    "\tSpecificationSet = demo_SPEC;\n"
    "\tLevel = __main__::demo_lvl;\n"
    "}\n\n"
    "TestCondition demo_hot\n{\n"
    "\tTestConditionGroup = demo_TCG;\n"
    "\tSelector = hot;\n"
    "}\n"
)

REAL = r"C:\Users\daithiby\source\repos\applications.manufacturing.ate-test.torch.client.nvl.sort.nvl-ngp-gdie192\Shared\Common"


class LvlTests(unittest.TestCase):
    def test_round_trip_is_identical(self):
        for text in (LVL, LVL.replace("\n", "\r\n"), LVL + "\n"):
            self.assertEqual(LvlDocument(text).text(), text)

    def test_structure(self):
        doc = LvlDocument(LVL)
        rows = doc.by_name["demo_lvl"].rows
        self.assertEqual([r.kind for r in rows], ["resource", "break", "resource", "break"])
        self.assertEqual(rows[0].props.keys(), ["IClampHi", "OPMode", "VForce"])
        self.assertEqual(rows[1].break_prop.value, "1mS")

    def test_edit_changes_only_that_line(self):
        doc = LvlDocument(LVL)
        before = doc.text().splitlines(True)
        doc.set_value(doc.by_name["demo_lvl"].rows[0].props["VForce"], "1.2V")
        after = doc.text().splitlines(True)
        changed = [i for i, (a, b) in enumerate(zip(before, after)) if a != b]
        self.assertEqual(len(changed), 1)
        self.assertEqual(after[changed[0]], "\t\tVForce = 1.2V;\n")
        self.assertTrue(doc.is_modified())

    def test_rejects_unsupported_and_bad_values(self):
        with self.assertRaises(LvlParseError):
            LvlDocument("Levels a\n{\n\tFoo(1)\n}\n")
        with self.assertRaises(LvlParseError):
            LvlDocument("Levels a\n{\n\tr\n\t{\n\t\tX = 1;\n")
        doc = LvlDocument(LVL)
        prop = doc.by_name["demo_lvl"].rows[0].props["VForce"]
        for bad in ("", "1V; Evil = 2", "a\nb"):
            with self.assertRaises(ValueError):
                doc.set_value(prop, bad)
        self.assertFalse(doc.is_modified())


class TcgAndExprTests(unittest.TestCase):
    def test_resolve_uses_selector_index(self):
        tcg = TcgDocument(TCG)
        _, group, spec, level, variables = tcg.resolve("demo_hot")
        self.assertEqual((group.name, level, spec.name), ("demo_TCG", "demo_lvl", "demo_SPEC"))
        self.assertEqual(variables["p_hi"], "0.9V")
        self.assertEqual(variables["p_ratio"], "2")

    def test_display_value(self):
        v = {"p_hi": "0.9V", "p_ratio": "2"}
        self.assertEqual(display_value('"VSIM"', v), "VSIM")
        self.assertEqual(display_value("TRUE", v), "True")
        self.assertEqual(display_value("500mA", v), "0.5")
        self.assertEqual(display_value("p_hi*p_ratio+100mV", v), "1.9")
        self.assertEqual(display_value("missing_var", v), "")


class ModelTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.mkdtemp()
        self.lvl, self.tcg = os.path.join(self.tmp, "a.lvl"), os.path.join(self.tmp, "a.tcg")
        for path, text in ((self.lvl, LVL), (self.tcg, TCG)):
            with io.open(path, "w", encoding="utf-8", newline="") as f:
                f.write(text)
        self.out = os.path.join(self.tmp, "out")

    def tearDown(self):
        shutil.rmtree(self.tmp)

    def test_edit_and_save_leaves_source_untouched(self):
        model = LevelsModel(self.lvl, self.tcg, self.out)
        view = model.open_condition("demo_hot")
        self.assertEqual(model.value_of(view.rows[0].row.props["IClampHi"], view.variables), "0.9")
        self.assertEqual(view.rows[1].label, "SequenceBreak [0]")
        model.set_value(view.rows[1].row.break_prop, "5mS")
        path = model.save()
        with io.open(path, newline="") as f:
            self.assertEqual(f.read(), LVL.replace("SequenceBreak 1mS", "SequenceBreak 5mS"))
        with io.open(self.lvl, newline="") as f:
            self.assertEqual(f.read(), LVL)

    def test_refuses_to_overwrite_source(self):
        model = LevelsModel(self.lvl, self.tcg, self.tmp)
        with self.assertRaises(ValueError):
            model.save()

    @unittest.skipUnless(os.path.isfile(os.path.join(REAL, "LevelsSequences.lvl")), "real files not present")
    def test_real_files_round_trip(self):
        model = LevelsModel(os.path.join(REAL, "LevelsSequences.lvl"), os.path.join(REAL, "BaseLevels.tcg"), self.out)
        with io.open(model.lvl_path, newline="") as f:
            self.assertEqual(model.lvl.text(), f.read())
        for name in model.condition_names:
            self.assertIn(model.tcg.resolve(name)[3], model.lvl.by_name)


USRV = (
    "Version 1.0;\r\n\r\n"
    "UserVars Grp\r\n{\r\n"
    "\tString Name = \"x\"; # trailing comment\r\n"
    "\t#String Commented = \"no\";\r\n"
    "\tDouble p_hi = 1;\r\n"
    "\tweird line here\r\n"
    "}\r\n\r\n"
    "SelectorRuleCollection Rules\r\n{\r\n\tSelectorRule R(a)\r\n\t{\r\n\t\ta => Grp.Name == \"x\";\r\n\t}\r\n}\r\n"
)


class LinkTests(unittest.TestCase):
    def test_usrv_index_is_fail_safe(self):
        idx = UsrvIndex(USRV)
        self.assertEqual(idx.by_qualified["Grp.Name"].line, 5)
        self.assertNotIn("Grp.Commented", idx.by_qualified)
        self.assertEqual([w[0] for w in idx.warnings], [8])
        self.assertEqual(len(idx.by_name["p_hi"]), 1)

    def test_find_names_skips_strings_numbers_and_units(self):
        f = LevelsModel.find_names
        self.assertEqual(f('p_hi*2 + Grp.Name - 500mA'), ["p_hi", "Grp.Name"])
        self.assertEqual(f('"Grp.Name"'), [])
        self.assertEqual(f("TRUE"), [])
        self.assertEqual(f("1e-3"), [])

    def test_locate_and_ambiguity(self):
        tmp = tempfile.mkdtemp()
        try:
            paths = {}
            for ext, text in (("lvl", LVL), ("tcg", TCG), ("usrv", USRV)):
                paths[ext] = os.path.join(tmp, "a." + ext)
                with io.open(paths[ext], "w", encoding="utf-8", newline="") as f:
                    f.write(text)
            m = LevelsModel(paths["lvl"], paths["tcg"], tmp + "x", paths["usrv"])
            spec = m.tcg.spec_sets["demo_SPEC"]
            ok = m.locate("Grp.Name")
            self.assertEqual((ok[0].label, ok[0].line), ("UservarDefinitions.usrv", 5))
            tcg_loc = m.locate("p_ratio", spec)
            self.assertEqual((tcg_loc[0].label, tcg_loc[0].line), ("BaseLevels.tcg", 6))
            self.assertEqual(len(m.locate("p_hi")), 2)  # tcg spec var and usrv var share a name
            self.assertEqual([r.status for r in m.references("p_hi + nope + Bad.Name")],
                             ["ambiguous", "unresolved", "unresolved"])
        finally:
            shutil.rmtree(tmp)


if __name__ == "__main__":
    unittest.main()


class UsrvEditTests(unittest.TestCase):
    def test_edit_is_single_line_and_round_trips(self):
        idx = UsrvIndex(USRV)
        self.assertEqual(idx.text(), USRV)
        var = idx.by_qualified["Grp.Name"]
        idx.set_value(var, "new val")
        new = idx.text()
        self.assertEqual(var.value, '"new val"')
        old_l, new_l = USRV.splitlines(True), new.splitlines(True)
        diff = [i for i in range(len(old_l)) if old_l[i] != new_l[i]]
        self.assertEqual(diff, [4])
        self.assertEqual(new_l[4], '\tString Name = "new val"; # trailing comment\r\n')
        idx.set_value(var, '"x"')
        self.assertEqual(idx.text(), USRV)

    def test_validation_and_refusals(self):
        idx = UsrvIndex(USRV)
        with self.assertRaises(ValueError):
            idx.set_value(idx.by_qualified["Grp.Name"], 'a"b')
        with self.assertRaises(ValueError):
            idx.set_value(idx.by_name["p_hi"][0], "abc")
        idx.set_value(idx.by_name["p_hi"][0], "2.5")
        self.assertEqual(idx.by_name["p_hi"][0].value, "2.5")
        bad = UsrvIndex("UserVars G\n{\n\tCurrent I = a_var*2A;\n\tOther X = 1;\n}\n")
        self.assertIsNotNone(bad.by_name["I"][0].edit_problem())
        self.assertIsNotNone(bad.by_name["X"][0].edit_problem())

    def test_write_back_backup_diff_and_stale_check(self):
        tmp = tempfile.mkdtemp()
        try:
            paths = {}
            for ext, text in (("lvl", LVL), ("tcg", TCG), ("usrv", USRV)):
                paths[ext] = os.path.join(tmp, "a." + ext)
                with io.open(paths[ext], "w", encoding="utf-8", newline="") as f:
                    f.write(text)
            out = os.path.join(tmp, "out")
            m = LevelsModel(paths["lvl"], paths["tcg"], out, paths["usrv"])
            self.assertEqual(m.pending_changes(), [])
            m.set_usrv_value(m.usrv.by_qualified["Grp.Name"], "zzz")
            m.save()
            self.assertEqual(open(paths["usrv"], "rb").read(), USRV.encode("utf-8"))  # save leaves source alone
            self.assertTrue(os.path.isfile(os.path.join(out, "a.usrv")))
            label, path, diff = m.pending_changes()[0]
            self.assertEqual([d[0] for d in diff], [5])
            # stale source is refused
            with io.open(paths["usrv"], "a", encoding="utf-8", newline="") as f:
                f.write("# touched\r\n")
            with self.assertRaises(ValueError):
                m.write_back()
            with io.open(paths["usrv"], "w", encoding="utf-8", newline="") as f:
                f.write(USRV)
            report = m.write_back()
            self.assertEqual(open(report[0][2], "rb").read(), USRV.encode("utf-8"))
            written = open(paths["usrv"], "rb").read()
            self.assertEqual(written, USRV.replace('"x"; #', '"zzz"; #').encode("utf-8"))
            self.assertFalse(os.path.exists(paths["usrv"] + ".tmp"))
        finally:
            shutil.rmtree(tmp)