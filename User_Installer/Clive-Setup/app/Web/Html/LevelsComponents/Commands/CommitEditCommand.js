import GridApiProvider from "../../BaseComponents/Services/GridApiProvider.js";
import BaseEvent from "../../BaseComponents/Events/BaseEvent.js";

// Finishes the cell editor that is still open (as pressing Enter would), so the host receives the typed
// value before it saves, closes or shows another table. Always answers with EditCommittedEvent.
// Args: { Token }
class CommitEditCommand {
    constructor(eventSender) {
        this.eventSender = eventSender;
    }

    run(args) {
        const gridApi = GridApiProvider.getGridApi();
        const wasEditing = gridApi ? gridApi.getEditingCells().length > 0 : false;
        const done = () => this.eventSender.postEvent(new BaseEvent("EditCommittedEvent", { Token: args.Token, WasEditing: wasEditing }));

        if (!wasEditing) {
            done();
            return;
        }

        gridApi.stopEditing(false);
        // The grid raises its edit event asynchronously; answer after it has been sent.
        setTimeout(() => setTimeout(done, 0), 0);
    }
}

export default CommitEditCommand;
