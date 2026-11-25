"""tkinter front end for the levels editor (dark grid + level-set list + details pane)."""
import argparse
import os
import sys
import tkinter as tk
from tkinter import ttk, messagebox, filedialog, simpledialog

from .model import LevelsModel
from .lvl import LvlParseError
from .tcg import TcgParseError

SRC = (r"C:\Users\daithiby\source\repos\applications.manufacturing.ate-test.torch.client.nvl.sort"
       r".nvl-ngp-gdie192\Shared\Common")
DEFAULT_LVL = os.path.join(SRC, "LevelsSequences.lvl")
DEFAULT_TCG = os.path.join(SRC, "BaseLevels.tcg")
DEFAULT_USRV = os.path.join(SRC, "UservarDefinitions.usrv")
DEFAULT_OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "output")

BG, PANEL, FG = "#1e1e1e", "#2b2b2b", "#e6e6e6"
HEAD, SEL, BREAK_BG, MOD_BG = "#1f4e79", "#2d6a9f", "#555555", "#7a6a00"
FIXED = ("row", "name", "delay", "resource")


class App(object):
    def __init__(self, root, model):
        self.root, self.model = root, model
        self.view = None
        self.shown_cols = []
        self.editor = None
        root.title("Levels Editor")
        root.geometry("1500x800")
        root.configure(bg=BG)
        self._style()
        self._build()
        self._fill_conditions()
        root.protocol("WM_DELETE_WINDOW", self._on_close)
        root.bind("<Control-f>", lambda e: (self.search_entry.focus_set(), self.search_entry.select_range(0, "end")))
        root.bind("<Control-s>", lambda e: self.save())

    def _style(self):
        s = ttk.Style()
        s.theme_use("clam")
        s.configure(".", background=PANEL, foreground=FG, fieldbackground=BG)
        s.configure("Treeview", background=BG, fieldbackground=BG, foreground=FG, rowheight=22, borderwidth=0)
        s.map("Treeview", background=[("selected", SEL)], foreground=[("selected", "white")])
        s.configure("Treeview.Heading", background=HEAD, foreground="white", relief="flat")
        s.map("Treeview.Heading", background=[("active", "#2a6aa8")])
        s.configure("TButton", background="#3a3a3a", foreground=FG, padding=4)
        s.configure("TCheckbutton", background=PANEL, foreground=FG)
        s.configure("TEntry", fieldbackground="#333333", foreground=FG, insertcolor=FG)
        s.configure("TLabel", background=PANEL, foreground=FG)
        s.configure("Dim.TLabel", background=PANEL, foreground="#9a9a9a")

    def _build(self):
        bar = ttk.Frame(self.root)
        bar.pack(fill="x")
        self.save_btn = ttk.Button(bar, text="Save", command=self.save)
        self.save_btn.pack(side="left", padx=2, pady=3)
        ttk.Button(bar, text="Reload", command=self.reload).pack(side="left", padx=2)
        ttk.Button(bar, text="Write back to source...", command=self.write_back).pack(side="left", padx=2)
        self.hide_empty = tk.BooleanVar(value=True)
        ttk.Checkbutton(bar, text="Hide Empty Columns", variable=self.hide_empty,
                        command=self._refresh_grid).pack(side="left", padx=10)
        ttk.Label(bar, text="Search Grid (Ctrl+F)").pack(side="left", padx=(20, 4))
        self.search_var = tk.StringVar()
        self.search_entry = ttk.Entry(bar, textvariable=self.search_var, width=28)
        self.search_entry.pack(side="left")
        self.search_entry.bind("<Return>", lambda e: self.find(1))
        self.search_entry.bind("<Shift-Return>", lambda e: self.find(-1))
        ttk.Button(bar, text="<", width=2, command=lambda: self.find(-1)).pack(side="left", padx=1)
        ttk.Button(bar, text=">", width=2, command=lambda: self.find(1)).pack(side="left")
        self.status = tk.StringVar()
        ttk.Label(bar, textvariable=self.status, style="Dim.TLabel").pack(side="right", padx=8)

        info = ttk.Frame(self.root)
        info.pack(fill="x", padx=6, pady=2)
        self.info = {}
        for col, (key, title) in enumerate((("group", "Test Condition Group:"), ("level", "Block:"),
                                            ("spec", "SpecSet:"), ("sel", "Selector:"))):
            ttk.Label(info, text=title).grid(row=0, column=col * 2, sticky="w", padx=(8, 2))
            var = tk.StringVar()
            ttk.Label(info, textvariable=var, style="Dim.TLabel").grid(row=0, column=col * 2 + 1, sticky="w")
            self.info[key] = var

        paned = tk.PanedWindow(self.root, orient="horizontal", bg=BG, sashwidth=4, bd=0)
        paned.pack(fill="both", expand=True)

        left = ttk.Frame(paned)
        self.filter_var = tk.StringVar()
        self.filter_var.trace("w", lambda *a: self._fill_conditions())
        ttk.Entry(left, textvariable=self.filter_var).pack(fill="x", padx=2, pady=2)
        self.cond_list = tk.Listbox(left, bg=BG, fg=FG, selectbackground=SEL, activestyle="none",
                                    highlightthickness=0, bd=0, exportselection=False, width=38)
        lsb = ttk.Scrollbar(left, command=self.cond_list.yview)
        self.cond_list.configure(yscrollcommand=lsb.set)
        lsb.pack(side="right", fill="y")
        self.cond_list.pack(fill="both", expand=True)
        self.cond_list.bind("<<ListboxSelect>>", self._on_condition)
        paned.add(left, minsize=180)

        mid = ttk.Frame(paned)
        self.grid = ttk.Treeview(mid, show="headings", selectmode="browse")
        vsb = ttk.Scrollbar(mid, command=self.grid.yview)
        hsb = ttk.Scrollbar(mid, orient="horizontal", command=self.grid.xview)
        self.grid.configure(yscrollcommand=vsb.set, xscrollcommand=hsb.set)
        vsb.pack(side="right", fill="y")
        hsb.pack(side="bottom", fill="x")
        self.grid.pack(fill="both", expand=True)
        self.grid.tag_configure("brk", background=BREAK_BG)
        self.grid.tag_configure("mod", background=MOD_BG)
        self.grid.bind("<<TreeviewSelect>>", lambda e: self._fill_details())
        self.grid.bind("<Double-1>", self._edit_grid_cell)
        paned.add(mid, minsize=400, stretch="always")

        right = ttk.Frame(paned)
        self.details_title = tk.StringVar()
        ttk.Label(right, textvariable=self.details_title).pack(fill="x", padx=4, pady=2)
        self.details = ttk.Treeview(right, columns=("name", "expr", "value"), show="headings", height=20)
        for cid, text, width in (("name", "Name", 130), ("expr", "Expression", 120), ("value", "Value", 100)):
            self.details.heading(cid, text=text)
            self.details.column(cid, width=width, anchor="w")
        self.details.tag_configure("mod", background=MOD_BG)
        self.details.pack(fill="both", expand=True)
        self.details.bind("<Double-1>", self._edit_details_cell)
        self.details.bind("<Control-Button-1>", self._ctrl_click_details)
        self.grid.bind("<Control-Button-1>", self._ctrl_click_grid, add="+")
        ttk.Label(right, text="Variables (click to open definition)", style="Dim.TLabel").pack(fill="x", padx=4)
        self.links = tk.Text(right, height=9, bg=BG, fg=FG, bd=0, highlightthickness=0, wrap="word",
                             cursor="arrow", state="disabled", font=("Segoe UI", 9))
        self.links.pack(fill="x")
        self.links.tag_configure("link", foreground="#5aa9ff", underline=True)
        self.links.tag_configure("mod", background=MOD_BG)
        self.links.tag_configure("bad", foreground="#e07070", underline=True)
        self.links.tag_bind("link", "<Enter>", lambda e: self.links.configure(cursor="hand2"))
        self.links.tag_bind("link", "<Leave>", lambda e: self.links.configure(cursor="arrow"))
        self.link_refs = {}
        self.viewer = None
        paned.add(right, minsize=300)

    # ---- data -> widgets -------------------------------------------------
    def _fill_conditions(self):
        text = self.filter_var.get().lower() if hasattr(self, "filter_var") else ""
        self.cond_names = [n for n in self.model.condition_names if text in n.lower()]
        self.cond_list.delete(0, "end")
        for n in self.cond_names:
            self.cond_list.insert("end", n)

    def _on_condition(self, _event=None):
        sel = self.cond_list.curselection()
        if not sel:
            return
        self.view = self.model.open_condition(self.cond_names[int(sel[0])])
        v = self.view
        self.info["group"].set(v.group.name if v.group else "")
        self.info["level"].set(v.level_name or "")
        self.info["spec"].set(v.spec_set.name if v.spec_set else "")
        self.info["sel"].set(v.condition.selector or "")
        if v.level_name not in self.model.lvl.by_name:
            self.status.set("Level %r not found in the .lvl file" % v.level_name)
        else:
            self.status.set("")
        self._refresh_grid()

    def _columns_for_view(self):
        props = self.model.property_columns
        if self.hide_empty.get() and self.view:
            used = set()
            for gr in self.view.rows:
                used.update(gr.row.props.keys())
            props = [p for p in props if p in used]
        return list(FIXED) + props

    def _refresh_grid(self):
        g = self.grid
        g.delete(*g.get_children())
        self.shown_cols = self._columns_for_view()
        g.configure(columns=list(FIXED) + self.model.property_columns, displaycolumns=self.shown_cols)
        for cid in list(FIXED) + self.model.property_columns:
            title = {"row": "#", "name": "Name", "delay": "Delay", "resource": "Resource"}.get(cid, cid)
            g.heading(cid, text=title)
            g.column(cid, width=40 if cid == "row" else 150 if cid == "name" else 90, anchor="w", stretch=False)
        if not self.view:
            return
        for gr in self.view.rows:
            self._insert_row(gr)
        self._fill_details()

    def _row_values(self, gr):
        if gr.is_break:
            base = [gr.index, gr.label, gr.row.break_prop.value, "Sequence Break"]
            return base + [""] * len(self.model.property_columns)
        base = [gr.index, gr.label, "", ""]
        return base + [gr.row.props[p].value if p in gr.row.props else ""
                       for p in self.model.property_columns]

    def _row_modified(self, gr):
        props = [gr.row.break_prop] if gr.is_break else gr.row.props.values()
        return any(p.modified for p in props)

    def _insert_row(self, gr):
        tags = ("brk",) if gr.is_break else ()
        if self._row_modified(gr):
            tags = tags + ("mod",)
        self.grid.insert("", "end", iid=str(gr.index), values=self._row_values(gr), tags=tags)

    def _selected_row(self):
        sel = self.grid.selection()
        if not sel or not self.view:
            return None
        return self.view.rows[int(sel[0]) - 1]

    def _fill_details(self):
        d = self.details
        d.delete(*d.get_children())
        gr = self._selected_row()
        self.details_title.set(gr.label if gr else "")
        if not gr:
            self._fill_links([])
            return
        props = [gr.row.break_prop] if gr.is_break else gr.row.props.values()
        for p in props:
            tags = ("mod",) if p.modified else ()
            d.insert("", "end", iid=p.name, tags=tags,
                     values=(p.name, p.value, self.model.value_of(p, self.view.variables)))
        self._fill_links(props)

    # ---- variable links --------------------------------------------------
    def _fill_links(self, props):
        t = self.links
        t.configure(state="normal")
        t.delete("1.0", "end")
        for tag in [x for x in t.tag_names() if x.startswith("ref")]:
            t.tag_delete(tag)
        self.link_refs = {}
        n = 0
        for p in props:
            refs = self.model.references(p.value, self.view.spec_set)
            if not refs:
                continue
            t.insert("end", p.name + ": ")
            for i, ref in enumerate(refs):
                tag = "ref%d" % n
                n += 1
                self.link_refs[tag] = ref
                t.insert("end", ref.name, ("bad" if ref.status == "unresolved" else "link", tag))
                if ref.status == "ok" and ref.locations[0].var is not None:
                    var = ref.locations[0].var
                    t.insert("end", " = %s " % var.value, ("mod",) if var.modified else ())
                    etag = "ref%d" % n
                    n += 1
                    self.link_refs[etag] = ref
                    t.insert("end", "[edit]", ("link", etag))
                    t.tag_bind(etag, "<Button-1>", lambda e, v=var: self._edit_uservar(v))
                t.tag_bind(tag, "<Button-1>", lambda e, tg=tag: self._follow(self.link_refs[tg], e))
                if i < len(refs) - 1:
                    t.insert("end", ", ")
            t.insert("end", "\n")
        t.configure(state="disabled")

    def _follow(self, ref, event=None):
        if not ref.locations:
            self.status.set("Cannot resolve %r: not defined in BaseLevels.tcg or UservarDefinitions.usrv" % ref.name)
            messagebox.showinfo("Variable not found",
                                "%r is not defined in BaseLevels.tcg%s." % (
                                    ref.name, " or UservarDefinitions.usrv" if self.model.usrv else
                                    " (no .usrv file loaded)"))
            return
        if len(ref.locations) == 1:
            self.show_source(ref.locations[0])
            return
        menu = tk.Menu(self.root, tearoff=0)
        for loc in ref.locations:
            menu.add_command(label="%s: %s (line %d)" % (loc.label, loc.description, loc.line),
                             command=lambda l=loc: self.show_source(l))
        x, y = (event.x_root, event.y_root) if event else (self.root.winfo_pointerx(), self.root.winfo_pointery())
        self.status.set("%r is defined in %d places; pick one" % (ref.name, len(ref.locations)))
        menu.tk_popup(x, y)

    def _follow_prop(self, prop, event):
        refs = self.model.references(prop.value, self.view.spec_set)
        if not refs:
            self.status.set("No variables in %s" % prop.value)
        elif len(refs) == 1:
            self._follow(refs[0], event)
        else:
            menu = tk.Menu(self.root, tearoff=0)
            for ref in refs:
                menu.add_command(label=ref.name, command=lambda r=ref: self._follow(r))
            menu.tk_popup(event.x_root, event.y_root)

    def _ctrl_click_grid(self, event):
        iid, col = self.grid.identify_row(event.y), self.grid.identify_column(event.x)
        if not iid or not col or not self.view:
            return
        prop = self.view.rows[int(iid) - 1].prop_for(self.shown_cols[int(col[1:]) - 1])
        if prop is not None:
            self._follow_prop(prop, event)
            return "break"

    def _ctrl_click_details(self, event):
        iid, gr = self.details.identify_row(event.y), self._selected_row()
        if iid and gr:
            self._follow_prop(gr.row.break_prop if gr.is_break else gr.row.props[iid], event)
            return "break"

    def show_source(self, loc):
        """Read-only viewer showing loc.path with loc.line highlighted."""
        if self.viewer is None or not self.viewer.winfo_exists():
            top = tk.Toplevel(self.root)
            top.geometry("1000x600")
            top.configure(bg=BG)
            self.viewer = top
            top.title_var = tk.StringVar()
            ttk.Label(top, textvariable=top.title_var).pack(fill="x")
            top.text = tk.Text(top, bg=BG, fg=FG, wrap="none", font=("Consolas", 10), bd=0, highlightthickness=0)
            ysb = ttk.Scrollbar(top, command=top.text.yview)
            xsb = ttk.Scrollbar(top, orient="horizontal", command=top.text.xview)
            top.text.configure(yscrollcommand=ysb.set, xscrollcommand=xsb.set)
            ysb.pack(side="right", fill="y")
            xsb.pack(side="bottom", fill="x")
            top.text.pack(fill="both", expand=True)
            top.text.tag_configure("hit", background="#8a6d00", foreground="white")
            top.path = None
        top = self.viewer
        text = top.text
        text.configure(state="normal")
        if top.path != loc.path:
            content = self.model.source_text(loc.path).replace("\r\n", "\n")
            text.delete("1.0", "end")
            text.insert("1.0", content)
            top.path = loc.path
        text.tag_remove("hit", "1.0", "end")
        text.tag_add("hit", "%d.0" % loc.line, "%d.0 lineend+1c" % loc.line)
        text.see("%d.0" % loc.line)
        total = int(text.index("end-1c").split(".")[0])
        text.yview_moveto(max(0.0, (loc.line - 8) / float(total)))
        text.configure(state="disabled")
        top.title("%s - read-only" % loc.label)
        top.title_var.set("%s  line %d: %s   [%s]" % (loc.label, loc.line, loc.description, loc.path))
        top.deiconify()
        top.lift()

    def _edit_uservar(self, var):
        problem = var.edit_problem()
        if problem:
            messagebox.showinfo("Can't edit", "%s: %s" % (var.qualified, problem))
            return
        values, sets = self.model.usrv_usage(var)
        prompt = ("%s (%s) - edit value in memory.\nShared: used by %d value(s) in %d level set(s)."
                  % (var.qualified, var.type, values, sets))
        text = simpledialog.askstring("Edit uservar", prompt, initialvalue=var.value, parent=self.root)
        if text is None or text.strip() == var.value:
            return
        try:
            self.model.set_usrv_value(var, text)
        except ValueError as exc:
            messagebox.showerror("Invalid value", str(exc))
            return
        if self.viewer is not None and self.viewer.winfo_exists():
            self.viewer.path = None
        self._fill_details()
        self.status.set("%s = %s (unsaved; used by %d values in %d level sets)" % (
            var.qualified, var.value, values, sets))

    def write_back(self):
        pending = self.model.pending_changes()
        if not pending:
            messagebox.showinfo("Write back", "No edits to write.")
            return
        lines = ["This OVERWRITES the original source files:", ""]
        for label, path, diff in pending:
            lines.append("%s  (%d line%s)\n  %s" % (label, len(diff), "" if len(diff) == 1 else "s", path))
            for no, old, new in diff[:8]:
                lines.append("  L%d: %s  ->  %s" % (no, old.strip(), new.strip()))
            if len(diff) > 8:
                lines.append("  ... and %d more" % (len(diff) - 8))
        for var in self.model.modified_usrv_vars():
            values, sets = self.model.usrv_usage(var)
            lines.append("")
            lines.append("WARNING: %s is shared by %d value(s) in %d level set(s)." % (var.qualified, values, sets))
        lines += ["", "A timestamped backup is written first. Continue?"]
        if not messagebox.askyesno("Write back to source files", "\n".join(lines), icon="warning", default="no"):
            return
        try:
            report = self.model.write_back()
        except (ValueError, IOError, OSError) as exc:
            messagebox.showerror("Write back failed", str(exc))
            return
        summary = []
        for label, path, backup, diff in report:
            summary.append("%s: %d line(s) changed (%s)\nbackup: %s" % (
                label, len(diff), ", ".join(str(d[0]) for d in diff), backup))
        m = self.model
        self.model = LevelsModel(m.lvl_path, m.tcg_path, m.output_dir, m.usrv_path)
        self.view = None
        self._fill_conditions()
        self._refresh_grid()
        self.status.set("Written back to source files")
        messagebox.showinfo("Written back", "\n\n".join(summary))

    # ---- editing ---------------------------------------------------------
    def _begin_edit(self, tree, iid, column_id, display_index, prop, commit):
        self._cancel_edit()
        bbox = tree.bbox(iid, display_index)
        if not bbox:
            return
        x, y, w, h = bbox
        var = tk.StringVar(value=prop.value)
        ent = tk.Entry(tree, textvariable=var, bg="#333333", fg=FG, insertbackground=FG,
                       relief="flat", highlightthickness=1)
        ent.place(x=x, y=y, width=max(w, 80), height=h)
        ent.focus_set()
        ent.select_range(0, "end")
        self.editor = ent

        def done(_e=None):
            if self.editor is not ent:
                return
            self.editor = None
            text = var.get()
            ent.destroy()
            if text.strip() != prop.value:
                try:
                    commit(prop, text)
                except ValueError as exc:
                    messagebox.showerror("Invalid value", str(exc))

        ent.bind("<Return>", done)
        ent.bind("<FocusOut>", done)
        ent.bind("<Escape>", lambda e: self._cancel_edit())

    def _cancel_edit(self):
        if self.editor is not None:
            ent, self.editor = self.editor, None
            ent.destroy()

    def _edit_grid_cell(self, event):
        if self.grid.identify_region(event.x, event.y) != "cell":
            return
        iid = self.grid.identify_row(event.y)
        col = self.grid.identify_column(event.x)
        if not iid or not col:
            return
        disp = int(col[1:]) - 1
        column = self.shown_cols[disp]
        gr = self.view.rows[int(iid) - 1]
        prop = gr.prop_for(column)
        if prop is None:
            self.root.bell()
            return
        self._begin_edit(self.grid, iid, column, col, prop, self._commit)

    def _edit_details_cell(self, event):
        if self.details.identify_region(event.x, event.y) != "cell" or self.details.identify_column(event.x) != "#2":
            return
        iid = self.details.identify_row(event.y)
        gr = self._selected_row()
        if not iid or not gr:
            return
        prop = gr.row.break_prop if gr.is_break else gr.row.props[iid]
        self._begin_edit(self.details, iid, "expr", "#2", prop, self._commit)

    def _commit(self, prop, text):
        self.model.set_value(prop, text)
        gr = self._selected_row()
        # the edited row is whichever row owns this property
        for r in self.view.rows:
            if prop in ([r.row.break_prop] if r.is_break else list(r.row.props.values())):
                self.grid.item(str(r.index), values=self._row_values(r),
                               tags=(("brk",) if r.is_break else ()) + (("mod",) if self._row_modified(r) else ()))
        if gr:
            self._fill_details()
        self.status.set("Modified (unsaved)")

    # ---- search / save / reload -----------------------------------------
    def find(self, step):
        q = self.search_var.get().strip().lower()
        if not q or not self.view:
            return
        rows = self.view.rows
        sel = self.grid.selection()
        start = int(sel[0]) - 1 if sel else (-1 if step > 0 else len(rows))
        for k in range(1, len(rows) + 1):
            idx = (start + step * k) % len(rows)
            vals = self._row_values(rows[idx])
            if any(q in str(v).lower() for v in vals):
                iid = str(rows[idx].index)
                self.grid.selection_set(iid)
                self.grid.see(iid)
                self.status.set("Found in row %s" % iid)
                return
        self.status.set("No match for %r" % q)

    def save(self):
        try:
            path = self.model.save()
        except (ValueError, IOError, OSError) as exc:
            messagebox.showerror("Save failed", str(exc))
            return
        self.status.set("Saved to %s" % path)

    def reload(self):
        if self.model.is_modified() and not messagebox.askyesno("Reload", "Discard unsaved edits?"):
            return
        m = self.model
        self.model = LevelsModel(m.lvl_path, m.tcg_path, m.output_dir, m.usrv_path)
        self.view = None
        self._fill_conditions()
        self._refresh_grid()
        self.status.set("Reloaded")

    def _on_close(self):
        if self.model.is_modified():
            answer = messagebox.askyesnocancel("Unsaved edits", "Save edits to the output folder before closing?")
            if answer is None:
                return
            if answer:
                self.save()
        self.root.destroy()


def _pick(path, title):
    if os.path.isfile(path):
        return path
    return filedialog.askopenfilename(title=title) or None


def main(argv=None):
    ap = argparse.ArgumentParser(description="Edit values in a Torch .lvl file (saves to the output folder).")
    ap.add_argument("--lvl", default=DEFAULT_LVL)
    ap.add_argument("--tcg", default=DEFAULT_TCG)
    ap.add_argument("--usrv", default=DEFAULT_USRV)
    ap.add_argument("--out", default=DEFAULT_OUT, help="folder that receives saved files")
    args = ap.parse_args(argv)
    root = tk.Tk()
    root.withdraw()
    lvl = _pick(args.lvl, "Select the .lvl file")
    tcg = _pick(args.tcg, "Select the .tcg file")
    if not lvl or not tcg:
        print("A .lvl and a .tcg file are required.")
        return 1
    try:
        model = LevelsModel(lvl, tcg, args.out, args.usrv if os.path.isfile(args.usrv) else None)
    except (LvlParseError, TcgParseError) as exc:
        messagebox.showerror("Unsupported file", "Cannot open safely: %s" % exc)
        return 1
    root.deiconify()
    App(root, model)
    root.mainloop()
    return 0


if __name__ == "__main__":
    sys.exit(main())
