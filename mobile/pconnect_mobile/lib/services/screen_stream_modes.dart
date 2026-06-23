/// Screen preview / streaming backends negotiated at WebSocket hello.
abstract final class ScreenStreamModes {
  static const String jpegV1 = 'jpeg-v1';
  static const String jpegBinV1 = 'jpeg-bin-v1';
  static const String webRtcV1 = 'webrtc-v1';

  /// Client preference order sent in `hello.screenStreamModes`.
  static List<String> clientPreference() {
    return const [webRtcV1, jpegBinV1, jpegV1];
  }
}
