/// Screen preview / streaming backends negotiated at WebSocket hello.
abstract final class ScreenStreamModes {
  static const String jpegV1 = 'jpeg-v1';
  static const String webRtcV1 = 'webrtc-v1';

  /// Build-time flag for future WebRTC preview (off in production).
  static const bool webRtcEnabled = bool.fromEnvironment(
    'PCONNECT_SCREEN_WEBRTC',
    defaultValue: false,
  );

  /// Client preference order sent in `hello.screenStreamModes`.
  static List<String> clientPreference() {
    return const [webRtcV1, jpegV1];
  }
}
