import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:shared_preferences/shared_preferences.dart';
import '../constants/theme_tokens.dart';
import '../services/connection.dart';
import '../services/groq_service.dart';
import '../services/speech_service.dart';
import '../services/tofu_pin_store.dart';
import '../services/voice_agent_service.dart';
import '../widgets/collapsible_section.dart';
import '../main.dart';
import 'discovery_screen.dart';
import 'diagnostics_screen.dart';

class SettingsScreen extends StatefulWidget {
  final PcConnection? conn;
  final ConnectionStatus status;
  final VoidCallback onDisconnect;
  final VoiceAgentService? voiceAgent;
  final SpeechService? speechService;

  const SettingsScreen({
    super.key,
    required this.conn,
    required this.status,
    required this.onDisconnect,
    this.voiceAgent,
    this.speechService,
  });

  @override
  State<SettingsScreen> createState() => _SettingsScreenState();
}

class _SettingsScreenState extends State<SettingsScreen> {
  bool _autoLock = false;
  double _sensitivity = 1.4;
  bool _invertScroll = false;
  bool _autoClipboardSync = true;
  List<ConnectionProfile> _profiles = [];

  // Voice assistant settings
  final _voiceAddressController = TextEditingController();
  final _voiceTokenController = TextEditingController();
  final _groqKeyController = TextEditingController();
  bool _testingVoiceConnection = false;

  @override
  void initState() {
    super.initState();
    _loadPrefs();
    _loadProfiles();
    _loadVoiceSettings();
  }

  @override
  void dispose() {
    _voiceAddressController.dispose();
    _voiceTokenController.dispose();
    _groqKeyController.dispose();
    super.dispose();
  }

  Future<void> _loadPrefs() async {
    final prefs = await SharedPreferences.getInstance();
    setState(() {
      _autoLock = prefs.getBool('auto_lock_on_disconnect') ?? false;
      _sensitivity = prefs.getDouble('trackpad_sensitivity') ?? 1.4;
      _invertScroll = prefs.getBool('invert_scroll') ?? false;
      _autoClipboardSync = prefs.getBool('auto_clipboard_sync') ?? true;
    });
  }

  Future<void> _loadProfiles() async {
    final profiles = await ProfileStore.load();
    if (mounted) setState(() => _profiles = profiles);
  }

  Future<void> _loadVoiceSettings() async {
    final agent = widget.voiceAgent;
    if (agent == null) return;
    await agent.loadSettings();
    final groqKey = await GroqService.loadApiKey();
    if (!mounted) return;
    setState(() {
      _voiceAddressController.text = agent.address ?? '';
      _voiceTokenController.text = agent.token ?? '';
      _groqKeyController.text = groqKey ?? '';
    });
  }

  Future<void> _saveVoiceSettings() async {
    final agent = widget.voiceAgent;
    if (agent == null) return;
    final address = _voiceAddressController.text.trim();
    final token = _voiceTokenController.text.trim();
    final groqKey = _groqKeyController.text.trim();
    await agent.saveSettings(address: address, token: token);
    if (groqKey.isNotEmpty) {
      await GroqService.saveApiKey(groqKey);
    } else {
      await GroqService.deleteApiKey();
    }
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      const SnackBar(content: Text('Voice assistant settings saved')),
    );
  }

  Future<void> _testVoiceConnection() async {
    final agent = widget.voiceAgent;
    if (agent == null) return;
    final address = _voiceAddressController.text.trim();
    final token = _voiceTokenController.text.trim();
    if (address.isEmpty || token.isEmpty) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Enter both address and token first')),
      );
      return;
    }
    setState(() => _testingVoiceConnection = true);
    final result = await agent.testConnection(address: address, token: token);
    if (!mounted) return;
    setState(() => _testingVoiceConnection = false);
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(result.connected
            ? 'Connected successfully!'
            : result.error ?? 'Connection failed'),
        backgroundColor: result.connected ? AppColors.success : null,
      ),
    );
  }

  Future<void> _clearVoiceSettings() async {
    final confirm = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('Clear Voice Settings'),
        content: const Text('This will remove the saved PC agent address, shared token, and Groq API key. Continue?'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('Cancel')),
          FilledButton(
            style: FilledButton.styleFrom(backgroundColor: AppColors.danger),
            onPressed: () => Navigator.pop(ctx, true),
            child: const Text('Clear All'),
          ),
        ],
      ),
    );

    if (confirm != true) return;

    _voiceAddressController.clear();
    _voiceTokenController.clear();
    _groqKeyController.clear();

    // Reset live voice state
    final speech = widget.speechService;
    if (speech != null) {
      speech.liveTranscript.value = '';
      speech.pipelineState.value = VoicePipelineState.idle;
      try { unawaited(speech.stopListening()); } catch (_) {}
      try { unawaited(speech.stopSpeaking()); } catch (_) {}
    }

    await widget.voiceAgent?.saveSettings(address: '', token: '');
    await GroqService.deleteApiKey();
    widget.voiceAgent?.disconnect();

    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      const SnackBar(content: Text('Voice settings cleared')),
    );
  }

  static const _tofuResetCooldownMs = 60000;

  Future<void> _resetTlsTrust() async {
    final prefs = await SharedPreferences.getInstance();
    final last = prefs.getInt('tofu_reset_cooldown_ms') ?? 0;
    final now = DateTime.now().millisecondsSinceEpoch;
    if (now - last < _tofuResetCooldownMs) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Please wait before resetting TLS trust again.')),
      );
      return;
    }

    await prefs.setInt('tofu_reset_cooldown_ms', now);
    await TofuPinStore.clearAllTrust();
    await TofuPinStore.primeFromDisk();
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      const SnackBar(content: Text('TLS trust cleared. Reconnect to the PC to confirm the new certificate.')),
    );
  }

  Future<void> _saveAutoLock(bool v) async {
    setState(() => _autoLock = v);
    final prefs = await SharedPreferences.getInstance();
    await prefs.setBool('auto_lock_on_disconnect', v);
    widget.conn?.settingsSync(autoLockOnDisconnect: v);
  }

  Future<void> _saveSensitivity(double v) async {
    setState(() => _sensitivity = v);
    final prefs = await SharedPreferences.getInstance();
    await prefs.setDouble('trackpad_sensitivity', v);
  }

  Future<void> _saveInvertScroll(bool v) async {
    setState(() => _invertScroll = v);
    final prefs = await SharedPreferences.getInstance();
    await prefs.setBool('invert_scroll', v);
  }

  Future<void> _saveAutoClipboardSync(bool v) async {
    setState(() => _autoClipboardSync = v);
    final prefs = await SharedPreferences.getInstance();
    await prefs.setBool('auto_clipboard_sync', v);
  }

  Future<void> _deleteProfile(int index) async {
    final p = _profiles[index];
    await ProfileStore.remove(p.ip, p.port);
    await _loadProfiles();
  }

  Future<void> _renameProfile(int index) async {
    final p = _profiles[index];
    final controller = TextEditingController(text: p.name);
    final name = await showDialog<String>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('Rename Profile'),
        content: TextField(
          controller: controller,
          autofocus: true,
          decoration: const InputDecoration(
            labelText: 'Profile name',
            border: OutlineInputBorder(),
          ),
        ),
        actions: [
          TextButton(onPressed: () => Navigator.pop(ctx), child: const Text('Cancel')),
          FilledButton(
            onPressed: () => Navigator.pop(ctx, controller.text.trim()),
            child: const Text('Save'),
          ),
        ],
      ),
    );
    controller.dispose();
    if (name != null && name.isNotEmpty) {
      p.name = name;
      final all = await ProfileStore.load();
      final idx = all.indexWhere((x) => x.ip == p.ip && x.port == p.port);
      if (idx >= 0) {
        all[idx].name = name;
        await ProfileStore.save(all);
        await _loadProfiles();
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;
    final connected = widget.status.connected;
    final themeCtrl = ThemeControllerScope.of(context);

    return Scaffold(
      appBar: AppBar(title: const Text('Settings')),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          // ── Connection ──
          CollapsibleSection(
            title: 'Connection',
            icon: Icons.wifi_rounded,
            storageKey: 'sett_conn',
            child: Column(
              children: [
                ListTile(
                  contentPadding: EdgeInsets.zero,
                  leading: Icon(connected ? Icons.link : Icons.link_off,
                      color: connected ? Colors.green : cs.error),
                  title: Text(connected ? 'Connected to ${widget.status.pcName ?? "PC"}' : 'Disconnected'),
                  subtitle: widget.status.role != null ? Text('Role: ${widget.status.role}') : null,
                  trailing: connected
                      ? TextButton(onPressed: widget.onDisconnect, child: const Text('Disconnect'))
                      : null,
                ),
                ListTile(
                  contentPadding: EdgeInsets.zero,
                  leading: const Icon(Icons.network_check_rounded),
                  title: const Text('Network diagnostics'),
                  subtitle: const Text('LAN, VPN, firewall, and agent ports'),
                  onTap: () => Navigator.of(context).push(MaterialPageRoute(
                    builder: (_) => DiagnosticsScreen(conn: widget.conn, status: widget.status),
                  )),
                ),
                ListTile(
                  contentPadding: EdgeInsets.zero,
                  leading: const Icon(Icons.refresh_rounded),
                  title: const Text('Reset PC TLS trust (TOFU)'),
                  subtitle: const Text('After PC cert change or reinstall. Next WSS reconnect re-learns fingerprint.'),
                  onTap: _resetTlsTrust,
                ),
              ],
            ),
          ),

          // ── Security ──
          CollapsibleSection(
            title: 'Security',
            icon: Icons.shield_rounded,
            storageKey: 'sett_sec',
            child: SwitchListTile(
              contentPadding: EdgeInsets.zero,
              title: const Text('Auto-lock on disconnect'),
              subtitle: const Text('Lock PC 10s after connection drops'),
              value: _autoLock,
              onChanged: connected ? _saveAutoLock : null,
            ),
          ),

          // ── Trackpad ──
          CollapsibleSection(
            title: 'Trackpad & Touch',
            icon: Icons.touch_app_rounded,
            storageKey: 'sett_tp',
            child: Column(
              children: [
                ListTile(
                  contentPadding: EdgeInsets.zero,
                  title: Row(
                    children: [
                      const Text('Sensitivity'),
                      const Spacer(),
                      Text(
                        _sensitivity.toStringAsFixed(1),
                        style: TextStyle(color: cs.primary, fontWeight: FontWeight.w600),
                      ),
                    ],
                  ),
                  subtitle: Slider(
                    value: _sensitivity,
                    min: 0.5,
                    max: 3.0,
                    divisions: 25,
                    label: _sensitivity.toStringAsFixed(1),
                    onChanged: (v) => _saveSensitivity(v),
                  ),
                ),
                SwitchListTile(
                  contentPadding: EdgeInsets.zero,
                  title: const Text('Invert scroll direction'),
                  value: _invertScroll,
                  onChanged: (v) => _saveInvertScroll(v),
                ),
              ],
            ),
          ),

          // ── Clipboard Sync ──
          CollapsibleSection(
            title: 'Clipboard Sync',
            icon: Icons.assignment_rounded,
            storageKey: 'sett_clip',
            child: Column(
              children: [
                SwitchListTile(
                  contentPadding: EdgeInsets.zero,
                  title: const Text('Auto-sync clipboard'),
                  subtitle: const Text('Automatically sync clipboard on connect'),
                  value: _autoClipboardSync,
                  onChanged: _saveAutoClipboardSync,
                ),
                ListTile(
                  contentPadding: EdgeInsets.zero,
                  leading: const Icon(Icons.content_paste_go_rounded),
                  title: const Text('Send phone clipboard to PC'),
                  onTap: connected
                      ? () async {
                          final data = await Clipboard.getData('text/plain');
                          if (data?.text != null && data!.text!.isNotEmpty) {
                            widget.conn?.setClipboard(text: data.text!);
                            if (context.mounted) {
                              ScaffoldMessenger.of(context).showSnackBar(
                                const SnackBar(content: Text('Clipboard sent to PC')),
                              );
                            }
                          }
                        }
                      : null,
                ),
                if (connected && widget.conn != null)
                  ValueListenableBuilder<List<String>>(
                    valueListenable: widget.conn!.clipboardHistoryNotifier,
                    builder: (context, history, _) {
                      if (history.isEmpty) return const SizedBox.shrink();
                      return Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Padding(
                            padding: const EdgeInsets.symmetric(vertical: 4),
                            child: Text('Recent Clipboard',
                                style: TextStyle(fontSize: 12, color: cs.outline)),
                          ),
                          ...history.take(5).map((text) => ListTile(
                                contentPadding: EdgeInsets.zero,
                                dense: true,
                                leading: const Icon(Icons.content_copy, size: 16),
                                title: Text(
                                  text.length > 80 ? '${text.substring(0, 80)}...' : text,
                                  style: const TextStyle(fontSize: 12),
                                  maxLines: 1,
                                  overflow: TextOverflow.ellipsis,
                                ),
                                onTap: () async {
                                  await Clipboard.setData(ClipboardData(text: text));
                                  if (context.mounted) {
                                    ScaffoldMessenger.of(context).showSnackBar(
                                      const SnackBar(content: Text('Copied to phone clipboard')),
                                    );
                                  }
                                },
                              )),
                        ],
                      );
                    },
                  ),
              ],
            ),
          ),

          // ── Saved Profiles ──
          CollapsibleSection(
            title: 'Saved PCs',
            icon: Icons.computer_rounded,
            storageKey: 'sett_pcs',
            child: Column(
              children: [
                if (_profiles.isEmpty)
                  const ListTile(
                    contentPadding: EdgeInsets.zero,
                    title: Text('No saved profiles'),
                    subtitle: Text('Connect to a PC to save it here'),
                  ),
                ...List.generate(_profiles.length, (i) {
                  final p = _profiles[i];
                  return ListTile(
                    contentPadding: EdgeInsets.zero,
                    leading: CircleAvatar(
                      backgroundColor: cs.secondaryContainer,
                      radius: 18,
                      child: Icon(Icons.computer_rounded, size: 18, color: cs.onSecondaryContainer),
                    ),
                    title: Text(p.name.isEmpty ? p.ip : p.name),
                    subtitle: Text('${p.ip}:${p.port}'),
                    trailing: PopupMenuButton<String>(
                      onSelected: (val) {
                        if (val == 'rename') _renameProfile(i);
                        if (val == 'delete') _deleteProfile(i);
                      },
                      itemBuilder: (_) => [
                        const PopupMenuItem(value: 'rename', child: Text('Rename')),
                        const PopupMenuItem(value: 'delete', child: Text('Forget')),
                      ],
                    ),
                  );
                }),
              ],
            ),
          ),

          // ── Appearance ──
          CollapsibleSection(
            title: 'Appearance',
            icon: Icons.palette_rounded,
            storageKey: 'sett_app',
            child: Padding(
              padding: const EdgeInsets.symmetric(vertical: 4),
              child: ValueListenableBuilder<ThemeMode>(
                valueListenable: themeCtrl,
                builder: (context, mode, _) {
                  return SegmentedButton<ThemeMode>(
                    segments: const [
                      ButtonSegment(
                        value: ThemeMode.dark,
                        label: Text('Dark'),
                        icon: Icon(Icons.dark_mode_rounded),
                      ),
                      ButtonSegment(
                        value: ThemeMode.light,
                        label: Text('Light'),
                        icon: Icon(Icons.light_mode_rounded),
                      ),
                      ButtonSegment(
                        value: ThemeMode.system,
                        label: Text('System'),
                        icon: Icon(Icons.settings_suggest_rounded),
                      ),
                    ],
                    selected: {mode},
                    onSelectionChanged: (newSelection) {
                      themeCtrl.setMode(newSelection.first);
                    },
                  );
                },
              ),
            ),
          ),

          // ── Voice Assistant ──
          if (widget.voiceAgent != null)
            CollapsibleSection(
              title: 'Voice Assistant',
              icon: Icons.mic_rounded,
              storageKey: 'sett_voice',
              defaultExpanded: false,
              child: Column(
                children: [
                  // Connection status
                  ValueListenableBuilder<VoiceAgentStatus>(
                    valueListenable: widget.voiceAgent!.statusNotifier,
                    builder: (context, status, _) {
                      final Color dotColor;
                      final String label;
                      switch (status.state) {
                        case VoiceAgentConnectionState.connected:
                          dotColor = Colors.green;
                          label = 'Voice agent connected';
                        case VoiceAgentConnectionState.connecting:
                        case VoiceAgentConnectionState.authenticating:
                          dotColor = Colors.orange;
                          label = 'Connecting…';
                        case VoiceAgentConnectionState.authFailed:
                          dotColor = cs.error;
                          label = 'Auth failed — check token';
                        case VoiceAgentConnectionState.disconnected:
                          dotColor = cs.error;
                          label = status.error ?? 'Not connected';
                      }
                      return ListTile(
                        contentPadding: EdgeInsets.zero,
                        leading: Icon(Icons.circle, size: 12, color: dotColor),
                        title: Text(label),
                      );
                    },
                  ),
                  const SizedBox(height: 8),
                  TextField(
                    controller: _voiceAddressController,
                    decoration: const InputDecoration(
                      labelText: 'PC Agent Address',
                      hintText: '192.168.1.42:8765',
                      helperText: 'ws:// connection — intended for trusted local Wi-Fi networks only',
                      helperMaxLines: 2,
                      border: OutlineInputBorder(),
                      prefixIcon: Icon(Icons.computer_rounded),
                    ),
                  ),
                  const SizedBox(height: 12),
                  TextField(
                    controller: _voiceTokenController,
                    obscureText: true,
                    decoration: const InputDecoration(
                      labelText: 'Shared Token',
                      border: OutlineInputBorder(),
                      prefixIcon: Icon(Icons.key_rounded),
                    ),
                  ),
                  const SizedBox(height: 12),
                  TextField(
                    controller: _groqKeyController,
                    obscureText: true,
                    decoration: const InputDecoration(
                      labelText: 'Groq API Key',
                      hintText: 'gsk_...',
                      border: OutlineInputBorder(),
                      prefixIcon: Icon(Icons.vpn_key_rounded),
                    ),
                  ),
                  const SizedBox(height: 12),
                  Row(
                    children: [
                      Expanded(
                        child: FilledButton.icon(
                          onPressed: _saveVoiceSettings,
                          icon: const Icon(Icons.save_rounded),
                          label: const Text('Save'),
                        ),
                      ),
                      const SizedBox(width: 8),
                      Expanded(
                        child: OutlinedButton.icon(
                          onPressed: _testingVoiceConnection ? null : _testVoiceConnection,
                          icon: _testingVoiceConnection
                              ? const SizedBox(
                                  width: 16, height: 16,
                                  child: CircularProgressIndicator(strokeWidth: 2),
                                )
                              : const Icon(Icons.wifi_find_rounded),
                          label: Text(_testingVoiceConnection ? 'Testing…' : 'Test'),
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: 8),
                  Align(
                    alignment: Alignment.centerRight,
                    child: TextButton.icon(
                      onPressed: _clearVoiceSettings,
                      style: TextButton.styleFrom(foregroundColor: AppColors.danger),
                      icon: const Icon(Icons.delete_outline_rounded, size: 18),
                      label: const Text('Clear Voice Settings'),
                    ),
                  ),
                ],
              ),
            ),

          // ── About ──
          CollapsibleSection(
            title: 'About Pconnect',
            icon: Icons.info_outline_rounded,
            storageKey: 'sett_about',
            child: const ListTile(
              contentPadding: EdgeInsets.zero,
              title: Text('Pconnect Agent & Client'),
              subtitle: Text('v0.2.0 • High-Performance LAN Remote Control'),
            ),
          ),
          const SizedBox(height: 24),
        ],
      ),
    );
  }
}
