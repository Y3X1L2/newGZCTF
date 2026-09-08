FROM postgres:16-alpine
RUN apk add --no-cache openssh && adduser -D qa && mkdir -p /run/sshd
ENTRYPOINT ["/bin/sh", "-c", "ssh-keygen -A >/dev/null && echo \"qa:$QA_PASSWORD\" | chpasswd && exec /usr/sbin/sshd -D -e -p 2222 -o PasswordAuthentication=yes -o 'Subsystem=sftp internal-sftp'"]
