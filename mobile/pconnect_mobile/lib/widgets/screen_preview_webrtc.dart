import 'package:flutter/material.dart';
import 'package:flutter_webrtc/flutter_webrtc.dart';

class ScreenPreviewWebRtc extends StatelessWidget {
  final RTCVideoRenderer renderer;
  final BoxFit fit;
  const ScreenPreviewWebRtc({super.key, required this.renderer, this.fit = BoxFit.contain});

  @override
  Widget build(BuildContext context) {
    return RTCVideoView(
      renderer,
      objectFit: fit == BoxFit.cover
          ? RTCVideoViewObjectFit.RTCVideoViewObjectFitCover
          : RTCVideoViewObjectFit.RTCVideoViewObjectFitContain,
      mirror: false,
    );
  }
}
