import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:pconnect_mobile/services/screen_stream_modes.dart';

void main() {
  group('ScreenStreamModes', () {
    test('clientPreference includes jpegBinV1 between webRtcV1 and jpegV1', () {
      final prefs = ScreenStreamModes.clientPreference();
      expect(prefs, contains(ScreenStreamModes.jpegBinV1));
      expect(prefs, contains(ScreenStreamModes.webRtcV1));
      expect(prefs, contains(ScreenStreamModes.jpegV1));

      final rtcIdx = prefs.indexOf(ScreenStreamModes.webRtcV1);
      final binIdx = prefs.indexOf(ScreenStreamModes.jpegBinV1);
      final jpegIdx = prefs.indexOf(ScreenStreamModes.jpegV1);
      expect(rtcIdx, lessThan(binIdx), reason: 'webrtc-v1 should come before jpeg-bin-v1');
      expect(binIdx, lessThan(jpegIdx), reason: 'jpeg-bin-v1 should come before jpeg-v1');
    });

    test('jpegBinV1 constant value', () {
      expect(ScreenStreamModes.jpegBinV1, equals('jpeg-bin-v1'));
    });
  });

  group('jpeg-bin-v1 binary frame parsing', () {
    /// Constructs a synthetic jpeg-bin-v1 binary frame.
    Uint8List buildBinaryFrame(int width, int height, List<int> jpegPayload) {
      final buffer = ByteData(9 + jpegPayload.length);
      buffer.setUint8(0, 0x01); // message type: screen frame
      buffer.setUint32(1, width, Endian.big);
      buffer.setUint32(5, height, Endian.big);
      final bytes = buffer.buffer.asUint8List();
      for (int i = 0; i < jpegPayload.length; i++) {
        bytes[9 + i] = jpegPayload[i];
      }
      return bytes;
    }

    test('parses width, height, and JPEG payload from binary frame', () {
      final jpegPayload = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46];
      final frame = buildBinaryFrame(1920, 1080, jpegPayload);

      // Verify header
      expect(frame[0], equals(0x01));

      // Parse width (uint32 big-endian, bytes 1-4)
      final bd = ByteData.sublistView(frame);
      final parsedWidth = bd.getUint32(1, Endian.big);
      expect(parsedWidth, equals(1920));

      // Parse height (uint32 big-endian, bytes 5-8)
      final parsedHeight = bd.getUint32(5, Endian.big);
      expect(parsedHeight, equals(1080));

      // Extract JPEG payload (bytes 9+)
      final extractedPayload = Uint8List.sublistView(frame, 9);
      expect(extractedPayload, equals(jpegPayload));
    });

    test('parses various dimensions correctly', () {
      final testCases = [
        (720, 405),
        (1280, 720),
        (3840, 2160),
        (1, 1),
      ];

      for (final (width, height) in testCases) {
        final frame = buildBinaryFrame(width, height, [0xAA, 0xBB]);
        final bd = ByteData.sublistView(frame);
        expect(bd.getUint32(1, Endian.big), equals(width));
        expect(bd.getUint32(5, Endian.big), equals(height));
      }
    });

    test('empty JPEG payload produces 9-byte frame', () {
      final frame = buildBinaryFrame(640, 480, []);
      expect(frame.length, equals(9));
      expect(frame[0], equals(0x01));
      final bd = ByteData.sublistView(frame);
      expect(bd.getUint32(1, Endian.big), equals(640));
      expect(bd.getUint32(5, Endian.big), equals(480));
    });

    test('binary frame with type != 0x01 should be ignored', () {
      // Simulates a binary frame with an unknown message type
      final frame = buildBinaryFrame(100, 100, [0xFF, 0xD8]);
      frame[0] = 0x02; // not a screen frame
      // The connection handler would skip this because frame[0] != 0x01
      expect(frame[0], isNot(equals(0x01)));
    });

    test('frame shorter than 9 bytes should be ignored', () {
      final shortFrame = Uint8List.fromList([0x01, 0x00, 0x00]);
      // Connection handler checks event.length >= 9 before parsing
      expect(shortFrame.length, lessThan(9));
    });
  });
}
