"""GUI-independent model: loads the .lvl + .tcg pair, builds grid views and saves edits."""
import datetime
import io
import os
import re

from .lvl import LvlDocument
from .tcg import TcgDocument
from .usrv import UsrvIndex
from .expr import display_value


class GridRow(object):
    def __init__(self, index, row, label):
        self.index = index
        self.row = row
        self.label = label

    @property
    def is_break(self):
        return self.row.kind == "break"

    def prop_for(self, column):
        """Property behind a grid column ('delay' or a property name), or None."""
        if self.is_break:
            return self.row.break_prop if column == "delay" else None
        return self.row.props.get(column)


class GridView(object):
    def __init__(self, condition, group, spec_set, level_name, variables, rows):
        self.condition = condition
        self.group = group
        self.spec_set = spec_set
        self.level_name = level_name
        self.variables = variables
        self.rows = rows


def _read(path):
    with io.open(path, "r", encoding="utf-8", newline="") as f:
        return f.read()


_QUOTED = re.compile(r'"[^"]*"')
_NAME = re.compile(r"(?<![\w.])[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)?(?![\w(])")
_KEYWORDS = ("TRUE", "FALSE")


class Location(object):
    """Where a variable is defined: file label, path, 1-based line, short description."""

    def __init__(self, label, path, line, description, var=None):
        self.label, self.path, self.line, self.description = label, path, line, description
        self.var = var  # UsrvVariable for .usrv definitions


class Reference(object):
    def __init__(self, name, locations):
        self.name = name
        self.locations = locations

    @property
    def status(self):
        return "unresolved" if not self.locations else "ambiguous" if len(self.locations) > 1 else "ok"


class LevelsModel(object):
    def __init__(self, lvl_path, tcg_path, output_dir, usrv_path=None):
        self.lvl_path = os.path.abspath(lvl_path)
        self.tcg_path = os.path.abspath(tcg_path)
        self.usrv_path = os.path.abspath(usrv_path) if usrv_path else None
        self.output_dir = os.path.abspath(output_dir)
        self.lvl = LvlDocument(_read(self.lvl_path))
        self._loaded = {self.lvl_path: self.lvl.text()}
        self.tcg = TcgDocument(_read(self.tcg_path))
        self.usrv = UsrvIndex(_read(self.usrv_path)) if self.usrv_path and os.path.isfile(self.usrv_path) else None
        if self.usrv:
            self._loaded[self.usrv_path] = self.usrv.text()
        names = set()
        for ls in self.lvl.level_sets:
            names.update(ls.property_names())
        self.property_columns = sorted(names, key=lambda s: s.lower())

    @staticmethod
    def find_names(value_text):
        """Variable names (``var`` or ``Group.Var``) used outside string literals, in order, unique."""
        text = _QUOTED.sub(lambda m: " " * len(m.group(0)), value_text)
        out = []
        for m in _NAME.finditer(text):
            n = m.group(0)
            if n.upper() not in _KEYWORDS and n not in out:
                out.append(n)
        return out

    def references(self, value_text, spec_set=None):
        return [Reference(n, self.locate(n, spec_set)) for n in self.find_names(value_text)]

    def locate(self, name, spec_set=None):
        """Definition sites for a name; several means ambiguous, none means unresolved."""
        if "." in name:
            var = self.usrv.by_qualified.get(name) if self.usrv else None
            return [self._usrv_loc(var)] if var else []
        if spec_set and name in spec_set.variables:
            return [self._tcg_loc(spec_set, spec_set.variables[name])]
        found = []
        for spec in self.tcg.spec_sets.values():
            if name in spec.variables:
                found.append(self._tcg_loc(spec, spec.variables[name]))
        if self.usrv:
            found.extend(self._usrv_loc(v) for v in self.usrv.by_name.get(name, []))
        return found

    def _tcg_loc(self, spec, var):
        return Location("BaseLevels.tcg", self.tcg_path, var.line, "%s (SpecificationSet %s)" % (var.name, spec.name))

    def _usrv_loc(self, var):
        return Location("UservarDefinitions.usrv", self.usrv_path, var.line, var.qualified, var)

    # ---- uservar editing -------------------------------------------------
    def set_usrv_value(self, var, text):
        return self.usrv.set_value(var, text)

    def source_text(self, path):
        """Text to show for a source file: in-memory (edited) for the .usrv, else read from disk."""
        if self.usrv and path == self.usrv_path:
            return self.usrv.text()
        return _read(path)

    def usrv_usage(self, var):
        """(values, level sets) in the .lvl whose expressions mention this uservar by name."""
        names = (var.name, var.qualified)
        values, sets = 0, 0
        for ls in self.lvl.level_sets:
            hit = 0
            for row in ls.rows:
                props = [row.break_prop] if row.kind == "break" else list(row.props.values())
                for p in props:
                    if any(n in names for n in self.find_names(p.value)):
                        hit += 1
            values += hit
            sets += 1 if hit else 0
        return values, sets

    def modified_usrv_vars(self):
        if not self.usrv:
            return []
        return sorted((v for vs in self.usrv.by_name.values() for v in vs if v.modified), key=lambda v: v.line)

    # ---- saving / write-back --------------------------------------------
    def _sources(self):
        """(label, path, loaded text, current text) for each source file that has edits."""
        out = []
        if self.lvl.is_modified():
            out.append(("LevelsSequences.lvl", self.lvl_path, self._loaded[self.lvl_path], self.lvl.text()))
        if self.usrv and self.usrv.is_modified():
            out.append(("UservarDefinitions.usrv", self.usrv_path, self._loaded[self.usrv_path], self.usrv.text()))
        return out

    @staticmethod
    def line_diff(old, new):
        """[(line, old line, new line)] for edits that keep the line count (all edits here do)."""
        a, b = old.splitlines(True), new.splitlines(True)
        if len(a) != len(b):
            raise ValueError("line count changed; refusing to continue")
        return [(i + 1, x.rstrip("\r\n"), y.rstrip("\r\n")) for i, (x, y) in enumerate(zip(a, b)) if x != y]

    def pending_changes(self):
        """[(label, path, [(line, old, new)])] for every edited source file."""
        return [(label, path, self.line_diff(old, new)) for label, path, old, new in self._sources()]

    def write_back(self, backup_dir=None, now=None):
        """Overwrite the edited source files in place, after a timestamped backup and a
        check that the on-disk file still equals what was loaded. Callers must confirm first.
        Returns [(label, path, backup path, [(line, old, new)])]."""
        sources = self._sources()
        for label, path, old, _new in sources:
            if _read(path) != old:
                raise ValueError("%s changed on disk since it was loaded; reload before writing back" % label)
        stamp = (now or datetime.datetime.now()).strftime("%Y%m%d-%H%M%S")
        backup_dir = os.path.abspath(backup_dir or os.path.join(os.path.dirname(self.output_dir), "output_backups"))
        if not os.path.isdir(backup_dir):
            os.makedirs(backup_dir)
        report = []
        for label, path, old, new in sources:
            diff = self.line_diff(old, new)
            backup = os.path.join(backup_dir, "%s.%s.bak" % (os.path.basename(path), stamp))
            with io.open(backup, "w", encoding="utf-8", newline="") as f:
                f.write(old)
            tmp = path + ".tmp"
            with io.open(tmp, "w", encoding="utf-8", newline="") as f:
                f.write(new)
            os.replace(tmp, path)
            report.append((label, path, backup, diff))
        return report

    @property
    def condition_names(self):
        return self.tcg.condition_names

    def open_condition(self, name):
        tc, group, spec, level, variables = self.tcg.resolve(name)
        level_set = self.lvl.by_name.get(level)
        rows, breaks = [], 0
        for i, row in enumerate(level_set.rows if level_set else []):
            if row.kind == "break":
                label = "SequenceBreak [%d]" % breaks
                breaks += 1
            else:
                label = row.name
            rows.append(GridRow(i + 1, row, label))
        return GridView(tc, group, spec, level, variables, rows)

    @staticmethod
    def value_of(prop, variables):
        return display_value(prop.value, variables)

    def set_value(self, prop, text):
        self.lvl.set_value(prop, text)

    def is_modified(self):
        return self.lvl.is_modified() or bool(self.usrv and self.usrv.is_modified())

    def output_path(self):
        return os.path.join(self.output_dir, os.path.basename(self.lvl_path))

    def save(self):
        """Write edited copies into the output folder; source files are never touched.
        Returns the .lvl copy's path (the .usrv copy is written alongside when edited)."""
        target = os.path.abspath(self.output_path())
        usrv_target = os.path.join(self.output_dir, os.path.basename(self.usrv_path)) if self.usrv_path else None
        for src, dst in ((self.lvl_path, target), (self.usrv_path, usrv_target)):
            if src and os.path.normcase(os.path.abspath(dst)) == os.path.normcase(src):
                raise ValueError("refusing to overwrite the source file")
        if not os.path.isdir(self.output_dir):
            os.makedirs(self.output_dir)
        with io.open(target, "w", encoding="utf-8", newline="") as f:
            f.write(self.lvl.text())
        if self.usrv and self.usrv.is_modified():
            with io.open(usrv_target, "w", encoding="utf-8", newline="") as f:
                f.write(self.usrv.text())
        return target
