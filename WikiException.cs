using System;

namespace Claymore.SharpMediaWiki
{
    public class WikiException : Exception
    {
        public WikiException()
        {
        }

        public WikiException(string message)
            : base(message)
        {
        }

        public WikiException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }

    public class LoginException : WikiException
    {
        public LoginException()
        {
        }

        public LoginException(string message)
            : base(message)
        {
        }

        public LoginException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }

    public class EditException : WikiException
    {
        public EditException()
        {
        }

        public EditException(string message)
            : base(message)
        {
        }

        public EditException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}
