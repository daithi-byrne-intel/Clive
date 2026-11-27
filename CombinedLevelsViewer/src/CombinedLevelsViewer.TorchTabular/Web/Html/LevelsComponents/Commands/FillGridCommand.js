import GridApiProvider from "../../BaseComponents/Services/GridApiProvider.js";
import GridElementProvider from "../../BaseComponents/Services/GridElementProvider.js";
import BaseEvent from "../../BaseComponents/Events/BaseEvent.js";
import IsOptionEnabled from "../../BaseComponents/Services/OptionsHelper.js";
import { LevelsCellOptions } from "../Consts/LevelsConsts.js";

// Levels counterpart of TorchTabularComponents/Commands/FillGridCommand.js.
// Args: { Records: [{ RowId, CellsData: { <column>: { Data, CellOptions } } }], GridState, DebugEnabled }
class FillGridCommand {
    constructor(columnDefsHelper, gridOptionsExtenders, eventSender) {
        this.columnDefsHelper = columnDefsHelper;
        this.gridOptionsExtenders = gridOptionsExtenders;
        this.eventSender = eventSender;
        this.contextMenuSuppressed = false;
    }

    run(args) {
        const { Records: records, GridState: state, DebugEnabled: debugEnabled } = args;

        const gridOptions = {
            theme: "legacy",
            defaultColDef: this.columnDefsHelper.createDefaultColDef(),
            columnDefs: this.columnDefsHelper.createColumnDefs(records),
            rowData: records,
            getRowId: (row) => row.data.RowId,
            tooltipShowDelay: 500,
            // Level property names are used as column fields and must never be read as paths.
            suppressFieldDotNotation: true,
            // Double-clicking a cell that cannot be edited would otherwise do nothing; let the host explain why.
            onCellDoubleClicked: (params) => {
                const cellData = this.columnDefsHelper.getCellValue(params);
                if (cellData !== undefined && IsOptionEnabled(cellData.CellOptions, LevelsCellOptions.Editable)) {
                    return;
                }
                this.eventSender.postEvent(new BaseEvent("CellNotEditableEvent", { RowId: params.data.RowId, ColumnName: params.colDef.field }));
            },
        };

        if (state !== null && state !== undefined) {
            gridOptions.initialState = state;
        }

        this.gridOptionsExtenders.forEach((extender) => extender.updateGridOptions(gridOptions));
        this.initializeGrid(gridOptions, debugEnabled);
    }

    initializeGrid(gridOptions, debugEnabled) {
        if (debugEnabled === false && !this.contextMenuSuppressed) {
            document.addEventListener("contextmenu", (e) => e.preventDefault());
            this.contextMenuSuppressed = true;
        }

        const previous = GridApiProvider.getGridApi();
        if (previous) {
            previous.destroy();
        }

        const gridDiv = GridElementProvider.getGridElement();
        gridDiv.innerHTML = "";
        GridApiProvider.setGridApi(agGrid.createGrid(gridDiv, gridOptions));
    }
}

export default FillGridCommand;
