import Bootstrapper from "../BaseComponents/Bootstrapper.js";
import CommandRunner from "../BaseComponents/Commands/CommandRunner.js";
import CommandsProvider from "../BaseComponents/Commands/CommandsProvider.js";
import EventSender from "../BaseComponents/Events/EventSender.js";
import CommandsMapper from "./Commands/CommandsMapper.js";
import ColumnDefsHelper from "./Services/ColumnDefsHelper.js";

// Same wiring as TorchTabularBootstrapper: Torch's base bootstrapper, command runner,
// theme/update commands and event sender are reused; only the levels-specific column
// helper and fill command are supplied here.
class LevelsTabularBootstrapper {
    static Init() {
        const columnDefHelper = new ColumnDefsHelper();
        const eventSender = new EventSender(true);
        const commandMapper = new CommandsMapper(eventSender, columnDefHelper);
        const commandsProvider = new CommandsProvider(commandMapper, columnDefHelper);
        const commandRunner = new CommandRunner(commandsProvider, eventSender);
        Bootstrapper.Init(commandRunner, eventSender);
    }
}

export default LevelsTabularBootstrapper;
