# **Command Overview**

### /admin
- Choose a user to grant or revoke admin privilieges to
- Can only be done by admins
- An admin can not revoke his own admin privileges

### /docker {command}
- Used to start, stop, or restart a docker container
- Admins are allowed to control every container
- Users can only start and stop containers that have been assigned to them by admins
- Start and stop permissions are granted separately
- Permissions can be done per user and for roles
- Example Command: /docker {command} --> hit enter --> choose the section --> choose the container

### /list
- This command lists all containers the user is allowed to interact with 
  - Includes the status (running or stopped)
- Admins see every container

### /permission
- Lists a users permissions
- Admins can enter a user or role for permissions to be shown

### /role
- Used to grant/revoke permissions to a role

### /user
- Used to grant/revoke permissions to a single user
