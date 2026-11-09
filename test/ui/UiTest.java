import javax.microedition.midlet.*;
import javax.microedition.lcdui.*;

public class UiTest extends MIDlet implements CommandListener {
    Display d; List menu; Form form; Command exit = new Command("Exit", Command.EXIT, 1), back = new Command("Back", Command.BACK, 1), ok = new Command("Save", Command.OK, 1), help = new Command("Help", Command.SCREEN, 2), about = new Command("About", Command.SCREEN, 3);
    protected void startApp() {
        d = Display.getDisplay(this);
        menu = new List("Lumidlet UI test", List.IMPLICIT);
        menu.append("Form", null); menu.append("Alert", null); menu.append("Text box", null);
        menu.addCommand(exit); menu.setCommandListener(this);
        form = new Form("Settings");
        form.append(new StringItem("Player", "This form shows every item type the emulator renders."));
        form.append(new TextField("Name", "Gil", 20, TextField.ANY));
        ChoiceGroup cg = new ChoiceGroup("Difficulty", Choice.EXCLUSIVE); cg.append("Easy", null); cg.append("Normal", null); cg.append("Hard", null);
        form.append(cg);
        ChoiceGroup mg = new ChoiceGroup("Options", Choice.MULTIPLE); mg.append("Sound", null); mg.append("Vibration", null);
        form.append(mg);
        form.append(new Gauge("Volume", true, 10, 7));
        form.addCommand(back); form.addCommand(ok); form.addCommand(help); form.addCommand(about); form.setCommandListener(this);
        d.setCurrent(menu);
    }
    protected void pauseApp() {}
    protected void destroyApp(boolean u) {}
    public void commandAction(Command c, Displayable s) {
        if (c == exit) { notifyDestroyed(); return; }
        if (c == back) { d.setCurrent(menu); return; }
        if (c == List.SELECT_COMMAND) {
            int i = menu.getSelectedIndex();
            if (i == 0) d.setCurrent(form);
            else if (i == 1) { Alert a = new Alert("Hello", "This is an alert that dismisses itself.", null, AlertType.INFO); a.setTimeout(1500); d.setCurrent(a, menu); }
            else { javax.microedition.lcdui.Displayable t = new TextBoxHolder().make(); t.addCommand(back); t.setCommandListener(this); d.setCurrent(t); }
        }
    }
}
