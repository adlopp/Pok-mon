using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace UltraYeaLauncher
{
    internal sealed class MainForm : Form
    {
        private readonly Label _status = new Label();
        private readonly TextBox _notes = new TextBox();
        private readonly ProgressBar _bar = new ProgressBar();
        private readonly Label _progressText = new Label();
        private readonly Button _btnUpdate = new Button();
        private readonly Button _btnPlay = new Button();
        private readonly Button _btnQuit = new Button();

        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        private LauncherConfig _cfg = new LauncherConfig();
        private UpdatePlan? _plan;
        private bool _updateApplied;   // la actualización se aplicó y version.txt ya está escrito

        public MainForm()
        {
            Text = "Pokémon Ultra Yea — Actualizador";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(500, 384);
            Font = new Font("Segoe UI", 9f);

            _status.SetBounds(16, 14, 468, 24);
            _status.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            _status.Text = "Comprobando actualizaciones…";

            _notes.SetBounds(16, 46, 468, 244);
            _notes.Multiline = true;
            _notes.ReadOnly = true;
            _notes.ScrollBars = ScrollBars.Vertical;
            _notes.BackColor = SystemColors.Window;
            _notes.TabStop = false;

            _bar.SetBounds(16, 300, 468, 16);
            _bar.Visible = false;

            _progressText.SetBounds(16, 320, 468, 16);
            _progressText.ForeColor = SystemColors.GrayText;

            _btnUpdate.SetBounds(16, 342, 200, 30);
            _btnUpdate.Text = "Actualizar y jugar";
            _btnUpdate.Enabled = false;
            _btnUpdate.Click += OnUpdateClick;

            _btnPlay.SetBounds(224, 342, 128, 30);
            _btnPlay.Text = "Jugar";
            _btnPlay.Enabled = false;
            _btnPlay.Click += (_, _) => LaunchAndExit();

            _btnQuit.SetBounds(392, 342, 92, 30);
            _btnQuit.Text = "Salir";
            _btnQuit.Click += (_, _) => Close();

            Controls.AddRange(new Control[] { _status, _notes, _bar, _progressText, _btnUpdate, _btnPlay, _btnQuit });

            FormClosing += (_, _) => _cts.Cancel();
            Shown += async (_, _) => await InitializeAsync().ConfigureAwait(true);
        }

        private string GameExePath => Path.Combine(LauncherConfig.Dir, string.IsNullOrWhiteSpace(_cfg.GameExe) ? "Game.exe" : _cfg.GameExe);

        // ------------------------------------------------------------- arranque

        private async Task InitializeAsync()
        {
            ResetForCheck();
            try
            {
                _cfg = LauncherConfig.Load();
                string local = _cfg.ReadLocalVersion();
                Log.Write($"Launcher iniciado. Versión local: {local}");

                using var gh = new GitHubClient();
                GhRelease rel = await gh.GetLatestReleaseAsync(_cfg, _cts.Token).ConfigureAwait(true);
                UpdateManifest? manifest = await gh.TryGetManifestAsync(rel, _cfg.ManifestAsset, _cts.Token).ConfigureAwait(true);
                _plan = Updater.ResolvePlan(_cfg, rel, manifest);
                Log.Write($"Versión publicada: {_plan.Version} (obligatoria={_plan.Mandatory})");

                if (VersionUtil.IsNewer(_plan.Version, local))
                {
                    _status.Text = $"Actualización disponible:   {local}  →  {_plan.Version}";
                    _notes.Text = FormatNotes(_plan.Notes);
                    _btnUpdate.Enabled = true;
                    _btnUpdate.Select();
                    _btnPlay.Enabled = _cfg.AllowSkipUpdate && !_plan.Mandatory && File.Exists(GameExePath);
                    if (_plan.Mandatory)
                        _progressText.Text = "Esta actualización es obligatoria para seguir jugando.";
                }
                else
                {
                    _status.Text = $"Tienes la última versión ({local}).";
                    _notes.Text = FormatNotes(_plan.Notes);
                    _btnPlay.Enabled = File.Exists(GameExePath);
                    if (_cfg.AutoLaunchWhenUpToDate && _btnPlay.Enabled)
                        LaunchAndExit();
                }
            }
            catch (OperationCanceledException)
            {
                // el usuario cerró la ventana
            }
            catch (Exception ex)
            {
                Log.Exception("comprobación de actualizaciones", ex);
                _status.Text = "No se pudo comprobar si hay actualizaciones.";
                _notes.Text = "Puedes jugar sin conexión con la versión instalada." +
                              Environment.NewLine + Environment.NewLine +
                              "Detalle técnico:" + Environment.NewLine + ex.Message;
                _btnPlay.Enabled = File.Exists(GameExePath);
                _btnUpdate.Text = "Reintentar";
                _btnUpdate.Enabled = true;
            }
        }

        private void ResetForCheck()
        {
            _status.Text = "Comprobando actualizaciones…";
            _notes.Text = "";
            _progressText.Text = "";
            _bar.Visible = false;
            _btnUpdate.Text = "Actualizar y jugar";
            _btnUpdate.Enabled = false;
            _btnPlay.Enabled = false;
        }

        // --------------------------------------------------------- actualizar

        private async void OnUpdateClick(object? sender, EventArgs e)
        {
            // Modo "Reintentar" tras un fallo de comprobación.
            if (_plan == null)
            {
                await InitializeAsync().ConfigureAwait(true);
                return;
            }

            _btnUpdate.Enabled = false;
            _btnPlay.Enabled = false;
            _btnQuit.Enabled = false;
            _bar.Visible = true;
            _bar.Style = ProgressBarStyle.Marquee;
            _updateApplied = false;

            var progress = new Progress<Updater.Progress>(p =>
            {
                _progressText.Text = p.Total > 0
                    ? $"{p.Phase}   {Bytes(p.Done)} / {Bytes(p.Total)}   ({p.Fraction:P0})"
                    : p.Phase;

                if (p.Total > 0)
                {
                    _bar.Style = ProgressBarStyle.Continuous;
                    _bar.Value = (int)Math.Round(p.Fraction * 100);
                }
                else
                {
                    _bar.Style = ProgressBarStyle.Marquee;
                }
            });

            try
            {
                using var gh = new GitHubClient();
                var updater = new Updater(gh.Raw, LauncherConfig.Dir);
                updater.EnsureGameDirWritable();
                await updater.RunAsync(_plan, _cfg.ReadLocalVersion(), progress, _cts.Token).ConfigureAwait(true);

                _cfg.WriteLocalVersion(_plan.Version);
                _updateApplied = true;
                Log.Write($"Actualización a {_plan.Version} completada.");
                _progressText.Text = "Actualización completada. Iniciando el juego…";
                LaunchAndExit();
            }
            catch (OperationCanceledException)
            {
                Close();
            }
            catch (Exception ex) when (_updateApplied)
            {
                // La actualización SÍ se aplicó (version.txt ya está en la versión
                // nueva); lo que falló fue abrir el juego después. No es un fallo
                // de actualización.
                Log.Exception("abrir el juego tras actualizar", ex);
                _bar.Visible = false;
                _status.Text = $"Actualización a {_plan.Version} aplicada correctamente.";
                _notes.Text = "El juego ya está actualizado." + Environment.NewLine + Environment.NewLine +
                              "No he podido abrirlo automáticamente. Cierra esta ventana y vuelve a " +
                              "abrir el launcher para jugar." + Environment.NewLine + Environment.NewLine +
                              "Detalle técnico: " + ex.Message;
                _btnUpdate.Enabled = false;
                _btnQuit.Enabled = true;
                _btnPlay.Enabled = File.Exists(GameExePath);
                _btnPlay.Select();
            }
            catch (Exception ex)
            {
                Log.Exception("actualización", ex);
                _bar.Visible = false;
                _status.Text = "La actualización ha fallado.";
                _notes.Text = ex.Message + Environment.NewLine + Environment.NewLine +
                              "Puedes reintentar o jugar con la versión actual.";
                _btnUpdate.Text = "Reintentar";
                _btnUpdate.Enabled = true;
                _btnQuit.Enabled = true;
                _btnPlay.Enabled = _cfg.AllowSkipUpdate && !_plan.Mandatory && File.Exists(GameExePath);
            }
        }

        // ---------------------------------------------------------- lanzar

        private void LaunchAndExit()
        {
            try
            {
                string exe = GameExePath;
                if (!File.Exists(exe))
                {
                    _status.Text = $"No encuentro {Path.GetFileName(exe)} junto al launcher.";
                    _btnPlay.Enabled = false;
                    return;
                }

                Process.Start(new ProcessStartInfo(exe)
                {
                    UseShellExecute = true,
                    WorkingDirectory = LauncherConfig.Dir,
                });
                Log.Write("Juego lanzado; el launcher se cierra.");
            }
            catch (Exception ex)
            {
                Log.Exception("lanzar el juego", ex);
                _status.Text = "No se pudo iniciar el juego: " + ex.Message;
                _btnQuit.Enabled = true;
                return;
            }

            Application.Exit();
        }

        // ---------------------------------------------------------- utilidades

        private static string FormatNotes(string notes)
            => string.IsNullOrWhiteSpace(notes)
                ? "(sin notas de versión)"
                : notes.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);

        private static string Bytes(long n)
        {
            string[] u = { "B", "KB", "MB", "GB" };
            double v = n;
            int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return $"{v:0.0} {u[i]}";
        }
    }
}
