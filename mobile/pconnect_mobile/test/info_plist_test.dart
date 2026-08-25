import 'dart:io';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('Info.plist exists, is well-formed XML, and contains required usage descriptions', () {
    final file = File('ios/Runner/Info.plist');
    expect(file.existsSync(), isTrue, reason: 'ios/Runner/Info.plist must exist');

    final content = file.readAsStringSync();

    // Verify structural XML markers
    expect(content, contains('<?xml version="1.0" encoding="UTF-8"?>'));
    expect(content, contains('<plist version="1.0">'));
    expect(content, contains('</plist>'));
    expect(content, contains('<dict>'));
    expect(content, contains('</dict>'));

    // Assert key presence for voice assistant features
    expect(content, contains('<key>NSMicrophoneUsageDescription</key>'),
        reason: 'NSMicrophoneUsageDescription key is missing from Info.plist');
    expect(content, contains('<key>NSSpeechRecognitionUsageDescription</key>'),
        reason: 'NSSpeechRecognitionUsageDescription key is missing from Info.plist');

    // Extract description strings following keys
    final micKeyIndex = content.indexOf('<key>NSMicrophoneUsageDescription</key>');
    final speechKeyIndex = content.indexOf('<key>NSSpeechRecognitionUsageDescription</key>');

    expect(micKeyIndex, isNot(-1));
    expect(speechKeyIndex, isNot(-1));

    final micSection = content.substring(micKeyIndex);
    final speechSection = content.substring(speechKeyIndex);

    // Verify <string> description text follows each key
    final micMatch = RegExp(r'<key>NSMicrophoneUsageDescription<\/key>\s*<string>([^<]+)<\/string>').firstMatch(micSection);
    final speechMatch = RegExp(r'<key>NSSpeechRecognitionUsageDescription<\/key>\s*<string>([^<]+)<\/string>').firstMatch(speechSection);

    expect(micMatch, isNotNull, reason: 'NSMicrophoneUsageDescription must have valid <string> content');
    expect(speechMatch, isNotNull, reason: 'NSSpeechRecognitionUsageDescription must have valid <string> content');

    expect(micMatch!.group(1)!.trim(), isNotEmpty);
    expect(speechMatch!.group(1)!.trim(), isNotEmpty);
  });
}
