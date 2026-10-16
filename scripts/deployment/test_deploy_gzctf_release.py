import importlib.util
import io
from pathlib import Path
import tempfile
import unittest
from unittest.mock import MagicMock, patch

spec = importlib.util.spec_from_file_location('deploy_release', Path(__file__).with_name('deploy-gzctf-release.py'))
deploy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(deploy)


class StageTests(unittest.TestCase):
    def run_stage(self, extra=(), checksum_status=0):
        with tempfile.TemporaryDirectory() as directory:
            archive = Path(directory) / 'release.tar.gz'
            archive.write_bytes(b'candidate archive')
            client = MagicMock()
            sftp = client.open_sftp.return_value
            sftp.stat.side_effect = FileNotFoundError
            sftp.open.return_value.__enter__.return_value = MagicMock()
            streams = []
            def execute(command, **kwargs):
                stdin = MagicMock()
                stdout = MagicMock()
                stdout.channel.recv_exit_status.return_value = checksum_status if 'sha256sum' in command else 0
                stderr = io.BytesIO(b'checksum mismatch')
                streams.append(stdin)
                return stdin, stdout, stderr
            client.exec_command.side_effect = execute
            argv = ['deploy', str(archive), '--host', 'example.test', '--stage-only', '--sudo-n', *extra]
            with patch.object(deploy.paramiko, 'SSHClient', return_value=client), patch('sys.argv', argv), patch.dict(deploy.os.environ, {}, clear=True):
                result = deploy.main()
            return result, client, streams

    def test_stage_with_key_does_not_activate_or_send_password(self):
        with tempfile.TemporaryDirectory() as directory:
            key = Path(directory) / 'key'
            key.touch()
            result, client, streams = self.run_stage(['--key-file', str(key)])
        self.assertEqual(0, result)
        client.load_system_host_keys.assert_called_once_with()
        self.assertIsInstance(client.set_missing_host_key_policy.call_args.args[0], deploy.paramiko.RejectPolicy)
        commands = [call.args[0] for call in client.exec_command.call_args_list]
        self.assertEqual(2, len(commands))
        self.assertTrue(commands[0].startswith('sudo -n install'))
        self.assertIn('/opt/gzctf/incoming', commands[0])
        self.assertIn('sha256sum -c -', commands[1])
        self.assertFalse(any('activate' in command or 'systemctl' in command or 'tar -x' in command for command in commands))
        client.open_sftp.return_value.put.assert_not_called()
        for stream in streams:
            stream.write.assert_not_called()
        self.assertIsNone(client.connect.call_args.kwargs['password'])
        self.assertEqual(str(key), client.connect.call_args.kwargs['key_filename'])

    def test_unknown_host_is_rejected(self):
        client = MagicMock()
        client.connect.side_effect = deploy.paramiko.SSHException('unknown host')
        with tempfile.TemporaryDirectory() as directory:
            archive = Path(directory) / 'release.tar.gz'
            archive.touch()
            with patch('sys.argv', ['deploy', str(archive), '--host', 'unknown.test', '--stage-only', '--sudo-n']), patch.object(deploy.paramiko, 'SSHClient', return_value=client):
                with self.assertRaises(deploy.paramiko.SSHException):
                    deploy.main()
        self.assertIsInstance(client.set_missing_host_key_policy.call_args.args[0], deploy.paramiko.RejectPolicy)
        client.open_sftp.assert_not_called()
        client.exec_command.assert_not_called()

    def test_checksum_failure_does_not_activate(self):
        with self.assertRaisesRegex(RuntimeError, 'checksum failed'):
            self.run_stage(checksum_status=1)

    def test_path_traversal_rejected_before_connect(self):
        with tempfile.TemporaryDirectory() as directory:
            archive = Path(directory) / 'release.tar.gz'
            archive.touch()
            with patch('sys.argv', ['deploy', str(archive), '--release-id', '../escape', '--plan-only']), patch.object(deploy.paramiko, 'SSHClient') as connect:
                with self.assertRaises(SystemExit):
                    deploy.main()
                connect.assert_not_called()


if __name__ == '__main__':
    unittest.main()
