// Must match LevelsRecordsExtractor on the host side.
class LevelsConsts {
    // Row header column: the resource / pin-group name, or the sequence-break label.
    // Contains characters that cannot appear in a level property name, so it cannot collide.
    static RowNameColumn = "Resource / Break";
}

// Same bit values as TorchTabularComponents/Consts/TabularCellOptions.js.
const LevelsCellOptions = {
    None: 0,
    Editable: 1,
    Invalid: 4,
};

export { LevelsConsts, LevelsCellOptions };
