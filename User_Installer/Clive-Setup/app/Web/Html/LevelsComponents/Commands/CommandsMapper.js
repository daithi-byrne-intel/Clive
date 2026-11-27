import FillGridCommand from "./FillGridCommand.js";
import CommitEditCommand from "./CommitEditCommand.js";
import CommandNameConsts from "./CommandNameConsts.js";
import CellEditHandler from "../../BaseComponents/Services/CellEditHandler.js";

class CommandsMapper {
    constructor(eventSender, columnDefHelper) {
        this.eventSender = eventSender;
        this.columnDefHelper = columnDefHelper;
    }

    SetCommands(mapping) {
        mapping.set(CommandNameConsts.FillGridCommand, this.createFillGridCommand());
        mapping.set(CommandNameConsts.CommitEditCommand, new CommitEditCommand(this.eventSender));
    }

    createFillGridCommand() {
        // Torch's CellEditHandler makes the grid read-only-edit: every edit is posted to the
        // host as a CellEditEvent and only shows up once the host sends an UpdateGridCommand.
        const gridOptionsExtenders = [
            new CellEditHandler(this.eventSender, this.columnDefHelper)
        ];
        return new FillGridCommand(this.columnDefHelper, gridOptionsExtenders, this.eventSender);
    }
}

export default CommandsMapper;
