import ColumnDefsHelperBase from "../../BaseComponents/Services/ColumnDefsHelperBase.js";
import IsOptionEnabled from "../../BaseComponents/Services/OptionsHelper.js";
import AgGridConsts from "../../BaseComponents/Consts/AgGridConsts.js";
import TabularStyles from "../../BaseComponents/Styles/TabularStyles.js";
import { LevelsConsts, LevelsCellOptions } from "../Consts/LevelsConsts.js";

// Levels counterpart of TorchTabularComponents/Services/ColumnDefsHelper.js.
// Columns are the union of the properties used by the selected level, in source order.
class ColumnDefsHelper extends ColumnDefsHelperBase {
    createDefaultColDef() {
        return {
            sortable: true,
            resizable: true,
            filter: true,
            minWidth: TabularStyles.minColumnWidth,
            editable: this.isEditable,
            valueGetter: this.valueGetter,
            tooltipValueGetter: this.tooltipValueGetter,
            cellStyle: this.cellStyle,
        };
    }

    // Per-group detail (for example which uservar each linked specification set resolves a clamp to).
    tooltipValueGetter = (params) => {
        const cellData = this.getCellValue(params);
        if (cellData === undefined) {
            return "";
        }
        return cellData.CustomToolTipMessage ? cellData.CustomToolTipMessage : cellData.Data;
    };

    // A clamp whose parameter has no exact uservar match is flagged in red.
    cellStyle = (params) => {
        const cellData = this.getCellValue(params);
        if (cellData !== undefined && IsOptionEnabled(cellData.CellOptions, LevelsCellOptions.Invalid)) {
            return { color: "#c62828", fontWeight: "bold" };
        }
        return { color: "", fontWeight: "" };
    };

    createColumnDef(column) {
        const columnDef = {
            field: column.name,
            headerName: column.name,
        };

        if (column.name === LevelsConsts.RowNameColumn) {
            columnDef.pinned = AgGridConsts.left;
            columnDef.lockPinned = true;
            columnDef.lockPosition = true;
            columnDef.width = TabularStyles.defaultPinnedColumnWidth;
        }

        return columnDef;
    }

    isEditable = (params) => {
        const cellData = this.getCellValue(params);
        return cellData !== undefined && IsOptionEnabled(cellData.CellOptions, LevelsCellOptions.Editable);
    };

    valueGetter = (params) => {
        const cellData = this.getCellValue(params);
        return cellData ? cellData.Data : "";
    };

    getCellValue = (params) => {
        if (params.data == undefined || params.data.CellsData == undefined) {
            return undefined;
        }
        return params.data.CellsData[params.colDef.field];
    };
}

export default ColumnDefsHelper;
