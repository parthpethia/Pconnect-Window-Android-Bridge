import 'dart:typed_data';
import 'package:flutter_webrtc/flutter_webrtc.dart';

class InputChannel {
  final RTCDataChannel _channel;

  InputChannel(this._channel);

  void sendMouseMove(int dx, int dy) {
    _send(0x01, dx, dy, 0);
  }

  void sendButtonDown(int button) {
    _send(0x02, 0, 0, button);
  }

  void sendButtonUp(int button) {
    _send(0x03, 0, 0, button);
  }

  void sendKey(int vk, int action, int extended) {
    _send(0x04, vk, action, extended);
  }

  void sendScroll(int dy) {
    _send(0x05, 0, dy, 0);
  }

  void _send(int type, int x, int y, int extra) {
    if (_channel.state != RTCDataChannelState.RTCDataChannelOpen) return;
    try {
      final data = ByteData(10);
      data.setUint8(0, type);
      data.setInt32(1, x, Endian.big);
      data.setInt32(5, y, Endian.big);
      data.setUint8(9, extra);
      _channel.send(RTCDataChannelMessage.fromBinary(data.buffer.asUint8List()));
    } catch (_) {
      // Drop silently on failure
    }
  }
}
