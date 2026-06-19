import 'package:flutter/material.dart';
import 'package:flutter_webrtc/flutter_webrtc.dart';

class ScreenPreviewWebRtc extends StatelessWidget {
  final RTCVideoRenderer renderer;
  const ScreenPreviewWebRtc({super.key, required this.renderer});

  @override
  Widget build(BuildContext context) {
    return RTCVideoView(
      renderer,
      objectFit: RTCVideoViewObjectFit.RTCVideoViewObjectFitContain,
      mirror: false,
    );
  }
}
