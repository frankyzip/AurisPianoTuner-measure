using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace AurisPianoTuner_measure
{
    public partial class Form1 : Form
    {
        // Placeholder handler so builds do not break when the diagnostics button is wired in the designer on other machines.
        private void btnDiagnostics_Click(object? sender, EventArgs e)
        {
            Debug.WriteLine("[Diagnostics] Button not implemented in this build.");
            MessageBox.Show(
                "Diagnosticsfunctionaliteit is nog niet beschikbaar in deze versie.",
                "Diagnostics",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }
}
