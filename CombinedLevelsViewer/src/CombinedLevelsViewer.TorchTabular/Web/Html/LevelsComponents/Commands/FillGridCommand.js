import GridApiProvider from "../../BaseComponents/Services/GridApiProvider.js";
import GridElementProvider from "../../BaseComponents/Services/GridElementProvider.js";

// Levels counterpart of TorchTabularComponents/Commands/FillGridCommand.js.
// Args: { Records: [{ RowId, CellsData: { <column>: { Data, CellOptions } } }], GridState, DebugEnabled }
class FillGridCommand {
    constructor(columnDefsHelper, gridOptionsExtenders) {
        this.columnDefsHelper = columnDefsHelper;
        this.gridOptionsExtenders = gridOptionsExtenders;
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
